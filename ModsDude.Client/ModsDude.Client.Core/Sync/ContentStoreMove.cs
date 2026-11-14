using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;

namespace ModsDude.Client.Core.Sync;

public sealed record ContentStoreMoveProgress(long BytesCopied, long TotalBytes);


/// <summary>
/// Moves a content store to another folder: a rename on one volume, a hash-checked copy across volumes.
/// </summary>
/// <remarks>
/// <paramref name="commit"/> writes the new folder down. It runs once everything is in the new
/// folder and before the old one is removed, so a cancel or failure before it leaves the old store
/// as it was and in use, and nothing after it can lose anything the store held.
/// </remarks>
internal sealed class ContentStoreMove(
    ContentStore source,
    string destination,
    Action commit,
    IProgress<ContentStoreMoveProgress>? progress,
    ILogger logger)
{
    /// <exception cref="UserFriendlyException">The destination holds other files, or lies inside the store.</exception>
    public async Task RunAsync(bool sameVolume, CancellationToken cancellationToken)
    {
        if (FileSystemHelper.ArePathsEqual(source.RootPath, destination) || source.IsMarked is false)
        {
            commit();

            return;
        }

        EnsureDestinationIsUsable();

        if (sameVolume)
        {
            Rename();
        }
        else
        {
            await CopyAsync(cancellationToken);
        }
    }


    private void EnsureDestinationIsUsable()
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.RootPath));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));

        if (FileSystemHelper.NormalizePathForComparison(target).StartsWith(FileSystemHelper.NormalizePathForComparison(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UserFriendlyException(
                "The store cannot move into itself",
                $"'{destination}' is inside the store at '{source.RootPath}', so the store was not moved.");
        }

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new UserFriendlyException(
                "That folder is not empty",
                $"'{destination}' already holds files, so the store at '{source.RootPath}' was not moved there.")
            {
                Hint = "Choose an empty folder."
            };
        }
    }

    private void Rename()
    {
        var existed = Directory.Exists(destination);

        if (existed)
        {
            Directory.Delete(destination);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);

        try
        {
            Directory.Move(source.RootPath, destination);
        }
        catch when (existed)
        {
            Directory.CreateDirectory(destination);

            throw;
        }

        try
        {
            commit();
        }
        catch
        {
            Directory.Move(destination, source.RootPath);

            throw;
        }
    }

    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        var existed = Directory.Exists(destination);
        var copy = new ContentStore(source.VolumeRoot, destination, source.MaxSizeBytes, logger);
        var entries = source.Enumerate();
        var quarantine = QuarantineFiles();
        var total = entries.Sum(x => x.Length) + quarantine.Sum(x => x.Length);
        long copied = 0;

        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var before = copied;

                if (await TryCopyBlobAsync(copy, entry, new BlobProgress(bytes => Report(before + bytes, total)), cancellationToken))
                {
                    copy.SetLastUsed(entry.Hash, entry.LastUsedUtc);
                }

                copied = before + entry.Length;
                Report(copied, total);
            }

            foreach (var file in quarantine)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = Path.Combine(copy.QuarantinePath, Path.GetRelativePath(source.QuarantinePath, file.FullName));

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyFileAsync(file.FullName, target, cancellationToken);

                copied += file.Length;
                Report(copied, total);
            }

            copy.DeleteTemporaryFiles();
            commit();
        }
        catch
        {
            FileSystemHelper.TryDeleteDirectory(destination, logger);

            if (existed)
            {
                Directory.CreateDirectory(destination);
            }

            throw;
        }

        // The move has happened: what is left is ModsDude's own copy of things the new store now holds.
        source.RemoveOwnFiles();
    }

    /// <summary>
    /// A blob that no longer hashes to its address is left behind rather than failing the move: it is
    /// re-downloadable by construction, and the old copy was useless anyway.
    /// </summary>
    private async Task<bool> TryCopyBlobAsync(ContentStore copy, ContentStoreEntry entry, IProgress<long> bytes, CancellationToken cancellationToken)
    {
        try
        {
            await copy.CopyFromAsync(source, entry.Hash, bytes, cancellationToken);

            return true;
        }
        catch (ContentVerificationException exception)
        {
            logger.LogWarning(exception, "Left the store blob {Hash} behind while moving the store to {Destination}: it no longer matches its address.", entry.Hash, destination);

            return false;
        }
    }

    /// <summary>A plain copy: the whole destination is removed if the move does not finish, so a short file never outlives it.</summary>
    private static async Task CopyFileAsync(string from, string to, CancellationToken cancellationToken)
    {
        await using (var reader = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var writer = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous))
        {
            await reader.CopyToAsync(writer, cancellationToken);
        }

        File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
    }

    private List<FileInfo> QuarantineFiles()

        => Directory.Exists(source.QuarantinePath)
            ? [.. new DirectoryInfo(source.QuarantinePath).EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(x => x.FullName, StringComparer.Ordinal)]
            : [];

    private void Report(long copied, long total) => progress?.Report(new ContentStoreMoveProgress(copied, total));


    private sealed class BlobProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
