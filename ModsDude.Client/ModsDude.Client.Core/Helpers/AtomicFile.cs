using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// Writes a file through a temporary beside it and moves that into place, so an interrupted write
/// leaves the previous file rather than a truncated one.
/// </summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        var temporaryPath = GetTemporaryPath(path);

        try
        {
            using (var stream = Create(temporaryPath))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception)
        {
            TryDelete(temporaryPath);

            throw;
        }
    }

    public static async Task WriteAsync(string path, Func<Stream, Task> write)
    {
        var temporaryPath = GetTemporaryPath(path);

        try
        {
            await using (var stream = Create(temporaryPath))
            {
                await write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception)
        {
            TryDelete(temporaryPath);

            throw;
        }
    }

    public static void WriteAllText(string path, string contents)
        => Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

            writer.Write(contents);
        });

    public static Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken)
        => WriteAsync(path, stream => stream.WriteAsync(contents, cancellationToken).AsTask());

    /// <summary>
    /// Deletes the temporaries interrupted writes to <paramref name="path"/> left beside it. Only for
    /// a caller whose writes to the path are serialised, since it would also delete a write in flight.
    /// </summary>
    public static void DeleteLeftovers(string path, ILogger log)
    {
        var folder = Path.GetDirectoryName(path);

        if (folder is null || Directory.Exists(folder) is false)
        {
            return;
        }

        var leftover = new Regex($"^{Regex.Escape(Path.GetFileName(path))}\\.[0-9a-f]{{32}}\\.tmp$", RegexOptions.IgnoreCase);

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, $"{Path.GetFileName(path)}.*.tmp").Where(x => leftover.IsMatch(Path.GetFileName(x))))
            {
                FileSystemHelper.TryDeleteFile(file, log);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(exception, "Could not look for leftover temporaries of {File}.", path);
        }
    }


    private static FileStream Create(string path)
        => new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous);

    private static string GetTemporaryPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // The write has already failed; a leftover temporary is the lesser problem.
        }
    }
}
