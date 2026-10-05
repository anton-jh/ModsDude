using ModsDude.Client.Core.Models;
using System.IO.Compression;

namespace ModsDude.Client.Core.GameAdapters;

/// <summary>
/// What every adapter whose mods are zip archives does the same way: scanning a folder of them,
/// opening one, and serving an image out of one.
/// </summary>
internal static class ModArchives
{
    private const string _archiveExtension = ".zip";


    /// <summary>
    /// Every mod archive directly in the folder, read in parallel and returned in the folder's
    /// order. Anything that is not a <c>.zip</c> is not a candidate at all and is never opened.
    /// </summary>
    /// <param name="skip">Files the caller already knows, left unopened.</param>
    /// <param name="read">One archive to a mod, or null where it is not one. Handles its own failures.</param>
    /// <remarks>
    /// Each file gets its own archive handle, so reading them in parallel is safe, and a mod folder can
    /// hold well over a thousand archives. The degree of parallelism is capped deliberately: this is
    /// disk bound, so a handful at a time is as quick as hundreds, and queueing one work item per file
    /// would hand the whole thread pool - which the rest of the app shares - to the scan for as long
    /// as it runs.
    /// </remarks>
    public static Task<IEnumerable<LocalMod>> ReadFolderAsync(
        string path,
        Func<string, bool> skip,
        Func<string, CancellationToken, LocalMod?> read,
        CancellationToken cancellationToken)
    {
        return Task.Run<IEnumerable<LocalMod>>(() =>
        {
            var files = Directory.EnumerateFiles(path)
                .Where(x => string.Equals(Path.GetExtension(x), _archiveExtension, StringComparison.OrdinalIgnoreCase) && skip(x) is false)
                .ToList();

            var mods = new LocalMod?[files.Count];

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            // Indexed rather than collected, so the results keep the order of the folder.
            Parallel.For(0, files.Count, options, i => mods[i] = read(files[i], cancellationToken));

            return mods.OfType<LocalMod>().ToList();
        }, cancellationToken);
    }

    public static ZipArchive Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch
        {
            // 'leaveOpen: false' only starts applying once the archive exists, so a constructor that
            // throws leaves the handle to close - once per non-zip in the folder, which in Downloads
            // is most of them.
            stream.Dispose();
            throw;
        }
    }

    /// <summary>An image inside a mod archive, read from the file again only when it is shown.</summary>
    public static ModImage Image(string modPath, ZipArchiveEntry entry)
    {
        // The entry belongs to an archive that is about to be closed - capture the name instead.
        var entryName = entry.FullName;
        var cacheKey = $"{modPath}|{entryName}|{entry.Length}|{entry.Crc32}";

        return new ModImage(entry.Name, cacheKey, async cancellationToken =>
        {
            await using var stream = new FileStream(modPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var target = archive.GetEntry(entryName)
                ?? throw new FileNotFoundException($"'{entryName}' is no longer present in '{modPath}'.");

            await using var entryStream = target.Open();
            using var buffer = new MemoryStream((int)target.Length);
            await entryStream.CopyToAsync(buffer, cancellationToken);

            return buffer.ToArray();
        });
    }
}
