namespace ModsDude.Client.Core.Tests.Sync;

/// <summary>
/// A real directory on a real filesystem, removed when the test is done.
/// </summary>
/// <remarks>
/// The store and the reconciler are almost entirely filesystem behaviour - hardlinks, link counts,
/// move semantics, timestamps - and a mocked filesystem would agree with whatever the test assumed.
/// So these tests run against the real one.
/// </remarks>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string name)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "modsdude-tests",
            $"{name}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path);
    }


    public string Path { get; }


    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string CreateSubdirectory(string name)
    {
        var path = Combine(name);
        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>Writes a file with deterministic contents, so its hash is a fact of the test.</summary>
    public string WriteFile(string relativePath, string content)
    {
        var path = Combine(relativePath);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception)
        {
            // A leftover temp directory is not worth failing a passing test over.
        }
    }
}
