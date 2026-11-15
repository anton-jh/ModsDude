using Microsoft.Extensions.Logging;
using ModsDude.Client.Wpf.Shared;

namespace ModsDude.Client.Wpf.Shell.Diagnostics;

/// <summary>
/// The log folder, for the error modal and the background-problem notice to point at.
/// </summary>
public static class LogFolder
{
    /// <inheritdoc cref="FileLoggerProvider.LogDirectory"/>
    public static string Path => FileLoggerProvider.LogDirectory;


    /// <summary>
    /// Shows the folder in Explorer. False where that failed, which leaves the caller to fall back
    /// to naming the path, rather than raising an error modal from inside an error modal.
    /// </summary>
    public static bool TryOpen(ILogger? logger = null)
    {
        try
        {
            ExplorerFolder.Open(Path);

            return true;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Could not open the log folder {Directory}.", Path);

            return false;
        }
    }
}
