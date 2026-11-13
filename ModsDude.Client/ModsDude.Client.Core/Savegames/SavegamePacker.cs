using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// One slot's contents, zipped and ready to upload.
/// </summary>
/// <param name="FilePath">
/// A temporary file the <b>caller owns</b>: nothing here deletes it, and nothing here will hand it
/// back a second time. Delete it once the upload has been verified, or on the way out of a failure.
/// </param>
/// <param name="ContentHash">
/// What the archive holds, as <see cref="SavegameContentHash"/> - the address the blob is stored under
/// and the value a check-in compares against the head.
/// </param>
/// <param name="SlotHash">
/// What the slot itself holds, which differs from <paramref name="ContentHash"/> only by the edits
/// that were applied to the archived copy alone.
/// </param>
/// <param name="ArchiveHash">The SHA-256 of the file's own bytes, which is what an upload verifies.</param>
/// <param name="EntryCount">How many files the archive holds. Zero is a slot with nothing in it.</param>
public readonly record struct PackedSavegame(
    string FilePath,
    string ContentHash,
    string SlotHash,
    string ArchiveHash,
    long SizeBytes,
    int EntryCount);


/// <summary>
/// Turns a savegame slot into an archive and back again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity is the contents, not the archive.</b> Every hash this hands out is a
/// <see cref="SavegameContentHash"/> over the files the adapter says belong in a packed save, so the
/// same slot hashes the same whoever packed it, on whatever runtime. A check-in whose hash equals the
/// head's mints no snapshot, and a drift check can only ask "has this been played?" if replaying the
/// same bytes answers no.
/// </para>
/// <para>
/// <b>Hashing and packing walk the same code.</b> <see cref="HashSlotAsync"/> is the pack's walk with
/// no archive to write, so a drift check cannot disagree with the packer.
/// </para>
/// </remarks>
public interface ISavegamePacker
{
    /// <summary>
    /// Zips everything in <paramref name="slot"/> that the adapter says belongs in a packed save,
    /// recursively, into a temporary file.
    /// </summary>
    /// <param name="overlay">
    /// Edits applied to the archived copy only; the slot on disk is not touched. An edit addressing a
    /// file outside the slot, or one the archive would not hold, is left out.
    /// </param>
    /// <param name="progress">Bytes of the slot's own files read so far, of the total found when the walk began.</param>
    Task<PackedSavegame> PackAsync(
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotId slot,
        IReadOnlyList<GameFileEdit> overlay,
        CancellationToken cancellationToken,
        IProgress<SavegameProgress>? progress = null);

    /// <summary>
    /// Replaces the slot's contents with the archive's, once they are verified to be
    /// <paramref name="expectedContentHash"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The slot ends up holding exactly the archive, and nothing else.</b> Whatever was there is
    /// renamed beside it and handed back: where it goes from there is the caller's decision. Nothing
    /// here deletes it.
    /// </para>
    /// <para>
    /// Any failure before the new contents are in place leaves the slot as it was. A kill between the
    /// two renames leaves a displaced folder <see cref="ISavegameRecycler.RecoverSlot"/> puts right.
    /// </para>
    /// </remarks>
    /// <returns>Where the slot's previous contents now are, or null where the slot was empty.</returns>
    /// <exception cref="InvalidDataException">
    /// An entry names a path outside the slot folder, or the contents are not the ones expected.
    /// </exception>
    Task<string?> UnpackAsync(
        string archivePath,
        string expectedContentHash,
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotId slot,
        CancellationToken cancellationToken,
        IProgress<SavegameProgress>? progress = null);

    /// <summary>
    /// What <see cref="PackAsync"/> would report as the slot hash. A slot that does not exist hashes
    /// as one holding nothing.
    /// </summary>
    Task<string> HashSlotAsync(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, CancellationToken cancellationToken);
}


/// <inheritdoc cref="ISavegamePacker"/>
public sealed class SavegamePacker(ILogger<SavegamePacker>? logger = null) : ISavegamePacker
{
    private readonly ILogger _log = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>
    /// Every entry's timestamp, in place of the file's own: a save copied between machines keeps its
    /// contents and loses its mtimes, and the archive should not carry a fact the hash ignores.
    /// </summary>
    private static readonly DateTimeOffset _fixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);


    public async Task<PackedSavegame> PackAsync(
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotId slot,
        IReadOnlyList<GameFileEdit> overlay,
        CancellationToken cancellationToken,
        IProgress<SavegameProgress>? progress = null)
    {
        var archivePath = GetTemporaryArchivePath();

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        try
        {
            Walk walk;
            string archiveHash;

            await using (var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous))
            {
                // Unseekable on purpose: ZipArchive patches local headers after the fact when it can
                // seek, and the hash taken while writing would then not be the hash of the file.
                await using var hashing = new HashingWriteStream(file);

                // leaveOpen, so the central directory has been written and counted before the hash is read.
                using (var archive = new ZipArchive(hashing, ZipArchiveMode.Create, leaveOpen: true))
                {
                    walk = await WalkAsync(adapter, target, slot, overlay, archive, progress, cancellationToken);
                }

                archiveHash = hashing.GetHash();
            }

            return new PackedSavegame(archivePath, walk.ContentHash, walk.SlotHash, archiveHash, new FileInfo(archivePath).Length, walk.EntryCount);
        }
        catch (Exception)
        {
            // The caller owns the file only once it has one. A half-written archive nobody was told
            // about is this method's to clean up - including on cancellation.
            FileSystemHelper.TryDeleteFile(archivePath, _log);

            throw;
        }
    }

    public async Task<string> HashSlotAsync(ILocalSavegameAdapter adapter, SavegameTarget target, SavegameSlotId slot, CancellationToken cancellationToken)
        => (await WalkAsync(adapter, target, slot, [], null, null, cancellationToken)).SlotHash;

    public async Task<string?> UnpackAsync(
        string archivePath,
        string expectedContentHash,
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotId slot,
        CancellationToken cancellationToken,
        IProgress<SavegameProgress>? progress = null)
    {
        var slotPath = Path.GetFullPath(adapter.GetSlotPath(target, slot));
        var parent = Path.GetDirectoryName(slotPath)
            ?? throw new ArgumentException($"'{slotPath}' is a filesystem root, not a savegame slot.", nameof(slot));
        var slotName = Path.GetFileName(slotPath);

        // Staged beside the slot rather than in the system temp folder, so landing it is a rename on
        // one volume instead of a second copy of a save that can be hundreds of megabytes.
        var staging = Path.Combine(parent, $"{StagingPrefix(slotName)}{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(staging);

            var contentHash = await ExtractAsync(archivePath, staging, progress, cancellationToken);

            if (contentHash != expectedContentHash)
            {
                throw new InvalidDataException(
                    $"The archive holds contents '{contentHash}' rather than the expected '{expectedContentHash}'. Nothing was written into the slot.");
            }

            if (Directory.Exists(slotPath) is false)
            {
                Directory.Move(staging, slotPath);

                return null;
            }

            var displaced = Path.Combine(parent, $"{DisplacedPrefix(slotName)}{Guid.NewGuid():N}");

            Directory.Move(slotPath, displaced);

            try
            {
                Directory.Move(staging, slotPath);
            }
            catch (Exception exception)
            {
                PutBack(displaced, slotPath, exception);

                throw;
            }

            return displaced;
        }
        finally
        {
            FileSystemHelper.TryDeleteDirectory(staging, _log);
        }
    }

    /// <summary>What a half-extracted copy of a slot is called, beside the slot.</summary>
    internal static string StagingPrefix(string slotFolderName) => $".modsdude-unpack-{slotFolderName}-";

    /// <summary>What a slot's previous contents are called between being moved aside and being put away.</summary>
    internal static string DisplacedPrefix(string slotFolderName) => $".modsdude-replaced-{slotFolderName}-";

    /// <summary>What unpacking the archive writes. Read from its central directory, so nothing is decompressed.</summary>
    internal static long MeasureUnpacked(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        return archive.Entries.Sum(x => x.Length);
    }

    internal static string TemporaryArchiveFolder => Path.Combine(Path.GetTempPath(), "modsdude", "savegames");

    internal static string GetTemporaryArchivePath()
        => Path.Combine(TemporaryArchiveFolder, $"{Guid.NewGuid():N}.zip");


    /// <summary>
    /// Returns a displaced save to its slot after the new one could not be put there. Where even that
    /// fails, the save is left where it was moved to, and the error says where.
    /// </summary>
    private static void PutBack(string displaced, string slotPath, Exception failure)
    {
        try
        {
            Directory.Move(displaced, slotPath);
        }
        catch (Exception restoreFailure)
        {
            throw new IOException(
                $"The new save could not be put in '{slotPath}', and the save that was there could not be moved back. It is at '{displaced}'.",
                new AggregateException(failure, restoreFailure));
        }
    }

    /// <summary>
    /// Reads every file of the slot once, hashing it and - where there is an archive - writing it in.
    /// </summary>
    private async Task<Walk> WalkAsync(
        ILocalSavegameAdapter adapter,
        SavegameTarget target,
        SavegameSlotId slot,
        IReadOnlyList<GameFileEdit> overlay,
        ZipArchive? archive,
        IProgress<SavegameProgress>? progress,
        CancellationToken cancellationToken)
    {
        var slotPath = Path.GetFullPath(adapter.GetSlotPath(target, slot));
        var files = EnumerateContents(adapter, slotPath);
        var edits = ResolveOverlay(adapter, slotPath, overlay);

        var total = files.Sum(x => x.Length);
        var done = 0L;

        // A file the game is still writing may grow past what the walk saw, which is why the count is
        // the caller's to clamp rather than something to promise here.
        Action<long>? copied = progress is null
            ? null
            : bytes => progress.Report(new SavegameProgress(SavegameStage.Packing, done += bytes, total));

        progress?.Report(new SavegameProgress(SavegameStage.Packing, 0, total));

        var onDisk = files.ToDictionary(x => x.RelativePath, x => x.FullPath, StringComparer.Ordinal);
        var paths = onDisk.Keys.Union(edits.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        var slotEntries = new List<(string, string)>();
        var contentEntries = new List<(string, string)>();

        foreach (var relativePath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = onDisk.GetValueOrDefault(relativePath);

            if (edits.TryGetValue(relativePath, out var fileEdits))
            {
                var original = fullPath is null ? null : await ReadSharedAsync(fullPath, cancellationToken);
                var edited = ApplyEdits(original, fileEdits, relativePath);

                if (original is not null)
                {
                    slotEntries.Add((relativePath, HashOf(original)));
                    copied?.Invoke(original.Length);
                }

                if (edited is not null)
                {
                    contentEntries.Add((relativePath, HashOf(edited)));

                    if (archive is not null)
                    {
                        await using var entryStream = CreateEntry(archive, relativePath);
                        await entryStream.WriteAsync(edited, cancellationToken);
                    }
                }

                continue;
            }

            // Shared for write and delete: the game may hold the save open, and a hash taken for a
            // drift check must never be the reason it fails to save.
            await using (var source = new FileStream(fullPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await using var entryStream = archive is null ? Stream.Null : CreateEntry(archive, relativePath);
                await using var hashing = new HashingWriteStream(entryStream);

                await ReportingCopy.CopyAsync(source, hashing, copied, cancellationToken);

                var hash = hashing.GetHash();

                slotEntries.Add((relativePath, hash));
                contentEntries.Add((relativePath, hash));
            }
        }

        return new Walk(SavegameContentHash.Of(slotEntries), SavegameContentHash.Of(contentEntries), contentEntries.Count);
    }

    private static Stream CreateEntry(ZipArchive archive, string relativePath)
    {
        var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
        entry.LastWriteTime = _fixedTimestamp;

        return entry.Open();
    }

    /// <summary>The bytes an edited file goes into the archive with. A failing edit leaves the file as it is.</summary>
    private byte[]? ApplyEdits(byte[]? original, IReadOnlyList<GameFileEdit> edits, string relativePath)
    {
        var current = original;

        foreach (var edit in edits)
        {
            try
            {
                current = edit.Transform(current) ?? current;
            }
            catch (Exception exception)
            {
                _log.LogWarning(exception, "The edit of {File} could not be applied to the packed copy; it is packed as it is.", relativePath);

                return original;
            }
        }

        return current;
    }

    /// <summary>The overlay's edits by the archive name they apply to, keeping only the ones the archive can hold.</summary>
    private Dictionary<string, IReadOnlyList<GameFileEdit>> ResolveOverlay(
        ILocalSavegameAdapter adapter, string slotPath, IReadOnlyList<GameFileEdit> overlay)
    {
        var resolved = new Dictionary<string, List<GameFileEdit>>(StringComparer.Ordinal);
        var prefix = slotPath.EndsWith(Path.DirectorySeparatorChar) ? slotPath : slotPath + Path.DirectorySeparatorChar;

        foreach (var edit in overlay)
        {
            var full = Path.IsPathRooted(edit.RelativePath) ? null : Path.GetFullPath(Path.Combine(slotPath, edit.RelativePath));
            var relativePath = full is not null && full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? ToArchivePath(Path.GetRelativePath(slotPath, full))
                : null;

            if (relativePath is null || adapter.BelongsInPackedSave(relativePath) is false)
            {
                _log.LogWarning("The edit of {File} is outside what a packed save holds, so the packed copy leaves it out.", edit.RelativePath);

                continue;
            }

            if (resolved.TryGetValue(relativePath, out var list) is false)
            {
                resolved[relativePath] = list = [];
            }

            list.Add(edit);
        }

        return resolved.ToDictionary(x => x.Key, x => (IReadOnlyList<GameFileEdit>)x.Value, StringComparer.Ordinal);
    }

    private static async Task<byte[]> ReadSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous);
        using var buffer = new MemoryStream();

        await source.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    private static string HashOf(byte[] content) => ModContentHasher.Format(SHA256.HashData(content));


    /// <summary>
    /// Every file under the slot that belongs in a packed save, as (archive name, path on disk),
    /// ordinal by archive name. The relative path is forward-slashed before the adapter is asked about
    /// it, so an adapter sees the same string the archive will store.
    /// </summary>
    private static IReadOnlyList<(string RelativePath, string FullPath, long Length)> EnumerateContents(ILocalSavegameAdapter adapter, string slotPath)
    {
        if (Directory.Exists(slotPath) is false)
        {
            return [];
        }

        return
        [
            .. new DirectoryInfo(slotPath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(x => (RelativePath: ToArchivePath(Path.GetRelativePath(slotPath, x.FullName)), x.FullName, x.Length))
                .Where(x => adapter.BelongsInPackedSave(x.RelativePath))
                .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
        ];
    }

    /// <returns>The <see cref="SavegameContentHash"/> of what was extracted.</returns>
    private static async Task<string> ExtractAsync(
        string archivePath,
        string root,
        IProgress<SavegameProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);

        var total = archive.Entries.Sum(x => x.Length);
        var done = 0L;
        var entries = new List<(string, string)>();

        Action<long>? copied = progress is null
            ? null
            : bytes => progress.Report(new SavegameProgress(SavegameStage.Unpacking, done += bytes, total));

        progress?.Report(new SavegameProgress(SavegameStage.Unpacking, 0, total));

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = ResolveWithin(root, entry.FullName);

            if (destination.HasValue is false)
            {
                // Zip slip. An archive is bytes from the server, and the day one of them says
                // '../../mods/x.zip' this is the line that decides whether unpacking a save can
                // write outside the slot it names.
                throw new InvalidDataException($"The archive entry '{entry.FullName}' would be written outside the savegame slot. Nothing was unpacked.");
            }

            // A name ending in a separator is a directory entry with no content. Nothing this packer
            // writes has one, but an archive is not required to have come from this packer.
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination.Value);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination.Value)!);

            await using var target = new FileStream(destination.Value, FileMode.Create, FileAccess.Write, FileShare.None, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous);
            await using var hashing = new HashingWriteStream(target);
            await using var source = entry.Open();

            await ReportingCopy.CopyAsync(source, hashing, copied, cancellationToken);

            entries.Add((entry.FullName, hashing.GetHash()));
        }

        return SavegameContentHash.Of(entries);
    }

    /// <summary>
    /// Where an entry lands, or <c>None</c> where that is anywhere but inside <paramref name="root"/>.
    /// Rooted names are refused before combining, because <see cref="Path.Combine(string, string)"/>
    /// discards the root it is given when the second part is absolute.
    /// </summary>
    private static Maybe<string> ResolveWithin(string root, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
        {
            return Maybe<string>.None;
        }

        var full = Path.GetFullPath(Path.Combine(root, entryName));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.Ordinal)
            ? full
            : Maybe<string>.None;
    }

    private static string ToArchivePath(string relativePath)
        => relativePath.Replace(Path.DirectorySeparatorChar, '/');


    private readonly record struct Walk(string SlotHash, string ContentHash, int EntryCount);
}
