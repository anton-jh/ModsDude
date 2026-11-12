using Microsoft.Extensions.Logging;

namespace ModsDude.Client.Core.Helpers;

public static class FileSystemHelper
{
    /// <summary>The buffer every file stream and copy loop in the client uses.</summary>
    public const int StreamBufferSize = 64 * 1024;


    /// <summary>
    /// Deletes a temporary or leftover file, logging rather than throwing where it will not go. Only
    /// for files the client itself created: anything of the user's is recycled, never deleted.
    /// </summary>
    public static void TryDeleteFile(string path, ILogger log)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            log.LogDebug(exception, "Could not delete {File}.", path);
        }
    }

    /// <inheritdoc cref="TryDeleteFile"/>
    public static void TryDeleteDirectory(string path, ILogger log)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception)
        {
            log.LogDebug(exception, "Could not delete {Directory}.", path);
        }
    }

    public static string GetAppDataDirectory()
    {
        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(localAppDataPath, AppIdentity.Name);
    }

    /// <summary>
    /// The volume a path lives on, in the form settings are keyed by: its mount point, through
    /// junctions, symbolic links and subst drives. Upper-cased rather than compared with a
    /// case-insensitive comparer, because the key survives a round trip through json which does not
    /// carry the dictionary's comparer.
    /// </summary>
    public static string NormalizeVolumeRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);

        return (Volumes.TryGetVolumeRoot(fullPath) ?? Path.GetPathRoot(fullPath) ?? path).ToUpperInvariant();
    }

    public static bool AreOnSameVolume(string left, string right)
        => string.Equals(NormalizeVolumeRoot(left), NormalizeVolumeRoot(right), StringComparison.Ordinal);

    /// <summary>
    /// Moves a file. On one volume that is a rename. Across volumes it is a cancellable copy to a
    /// staging file beside the destination, moved into place once complete, and then the source is
    /// deleted - so the destination never holds a short file and a cancelled move leaves the source.
    /// </summary>
    public static async Task MoveFileAsync(string source, string destination, ILogger log, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(destination)!;

        if (AreOnSameVolume(source, folder))
        {
            File.Move(source, destination);

            return;
        }

        var staging = Path.Combine(folder, $"{Guid.NewGuid():N}{StagingExtension}");

        try
        {
            await using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var to = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, StreamBufferSize, FileOptions.Asynchronous))
            {
                await ReportingCopy.CopyAsync(from, to, null, cancellationToken);
            }

            File.SetLastWriteTimeUtc(staging, File.GetLastWriteTimeUtc(source));
            File.Move(staging, destination);
        }
        catch
        {
            TryDeleteFile(staging, log);

            throw;
        }

        File.Delete(source);
    }

    /// <summary>What a file ModsDude is still copying into a folder is called, so a crash's leftovers can be told apart.</summary>
    public const string StagingExtension = ".modsdude-copy";

    /// <summary>
    /// Whether two paths name the same location. Case-insensitive and separator-insensitive, which
    /// is what Windows means by the same folder.
    /// </summary>
    public static bool ArePathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(NormalizePathForComparison(left), NormalizePathForComparison(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A path in the form two references to the same folder always agree on. Lower-cased rather than
    /// left to a case-insensitive comparer, because callers use it to build keys that outlive the
    /// comparer - json does not carry one.
    /// </summary>
    public static string NormalizePathForComparison(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToLowerInvariant();
    }

    /// <summary>
    /// Moves a folder, across volumes where it has to. A cross-volume move copies everything first:
    /// a copy that fails part way is removed and the source is left as it was, and once the copy is
    /// complete the move has happened - removing the source is tidying, logged where it fails.
    /// </summary>
    /// <exception cref="Exception">The folder was not moved, and is still at <paramref name="source"/>.</exception>
    public static void MoveDirectory(string source, string destination, ILogger log)
    {
        var fullSource = Path.GetFullPath(source);
        var fullDestination = Path.GetFullPath(destination);

        // Directory.Move refuses paths whose roots differ as text, even when a junction or subst drive puts them on one volume.
        if (string.Equals(Path.GetPathRoot(fullSource), Path.GetPathRoot(fullDestination), StringComparison.OrdinalIgnoreCase)
            && AreOnSameVolume(fullSource, Path.GetDirectoryName(fullDestination)!))
        {
            Directory.Move(source, destination);

            return;
        }

        try
        {
            CopyDirectory(source, destination);
        }
        catch (Exception)
        {
            TryDeleteDirectory(destination, log);

            throw;
        }

        try
        {
            Directory.Delete(source, recursive: true);
        }
        catch (Exception exception)
        {
            log.LogWarning(exception, "Moved {Source} to {Destination}, but could not remove what was left at {Source}.", source, destination, source);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    /// <summary>
    /// A path in <paramref name="folder"/> for <paramref name="fileName"/> that nothing is using: the name as
    /// given where it is free, and otherwise with a counter before the extension - <c>mod (2).zip</c>, the
    /// way Explorer does it, so that a move never overwrites what a previous one put there.
    /// </summary>
    public static string GetUnusedPath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);

        if (File.Exists(candidate) is false && Directory.Exists(candidate) is false)
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var counter = 2; ; counter++)
        {
            candidate = Path.Combine(folder, $"{stem} ({counter}){extension}");

            if (File.Exists(candidate) is false && Directory.Exists(candidate) is false)
            {
                return candidate;
            }
        }
    }
}
