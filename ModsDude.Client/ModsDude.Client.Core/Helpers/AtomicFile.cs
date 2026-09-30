using System.Text;

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
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
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

    public static void WriteAllText(string path, string contents)
        => Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

            writer.Write(contents);
        });

    public static async Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        var temporaryPath = GetTemporaryPath(path);

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, contents, cancellationToken);

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception)
        {
            TryDelete(temporaryPath);

            throw;
        }
    }


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
