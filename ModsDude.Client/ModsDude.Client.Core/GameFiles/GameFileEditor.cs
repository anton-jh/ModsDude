using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.GameFiles;

public sealed class GameFileEditor(IRecycleBin recycleBin, ILogger<GameFileEditor> logger) : IGameFileEditor
{
    public PlannedFileEdit Plan(string folder, GameFileEdit edit)
    {
        var path = Resolve(folder, edit);
        var current = ReadIfExists(path);

        return new PlannedFileEdit(edit, path, Changed(current, edit.Transform(current)) is not null);
    }

    public bool Apply(string folder, GameFileEdit edit)
    {
        var path = Resolve(folder, edit);
        var current = ReadIfExists(path);

        if (Changed(current, edit.Transform(current)) is not byte[] replacement)
        {
            return false;
        }

        if (edit.RecycleReplaced && current is not null)
        {
            RecycleCopyOf(path, current);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, stream => stream.Write(replacement));

        return true;
    }


    private static string Resolve(string folder, GameFileEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.RelativePath) || Path.IsPathRooted(edit.RelativePath))
        {
            throw new ArgumentException($"An edit's path must be relative, not '{edit.RelativePath}'.", nameof(edit));
        }

        return Path.GetFullPath(Path.Combine(folder, edit.RelativePath));
    }

    private static byte[]? ReadIfExists(string path)
        => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static byte[]? Changed(byte[]? current, byte[]? desired)
        => desired is null || (current is not null && current.AsSpan().SequenceEqual(desired)) ? null : desired;

    /// <summary>
    /// Recycles a copy under the file's own name, before the file is replaced, so the Recycle Bin
    /// holds the old content whatever happens to the write.
    /// </summary>
    private void RecycleCopyOf(string path, byte[] content)
    {
        var folder = Path.Combine(Path.GetTempPath(), "modsdude", "recycle", Guid.NewGuid().ToString("N"));
        var copy = Path.Combine(folder, Path.GetFileName(path));

        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllBytes(copy, content);

            if (recycleBin.TryRecycle(copy) is false)
            {
                throw new IOException($"The current '{Path.GetFileName(path)}' could not be put in the Recycle Bin, so it was left unchanged.");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not remove the temporary folder {Folder}.", folder);
            }
        }
    }
}
