using System.Diagnostics;
using System.IO;

namespace ModsDude.Client.Wpf.Shared;

public static class ExplorerFolder
{
    /// <summary>
    /// Shows the folder in Explorer. Checks it is a folder first, so a path that has become a file is
    /// never opened or run.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public static void Open(string path)
    {
        if (Directory.Exists(path) is false)
        {
            throw new DirectoryNotFoundException($"The folder {path} does not exist.");
        }

        using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
