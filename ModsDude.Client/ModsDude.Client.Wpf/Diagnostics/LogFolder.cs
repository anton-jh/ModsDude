using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ModsDude.Client.Wpf.Diagnostics;

/// <summary>
/// Opening the log folder in Explorer, in the one place that knows how.
/// </summary>
/// <remarks>
/// Two things offer it - the error dialog and the background-problem notice - and both are telling
/// somebody "the rest is in the log". A second copy of this would be a second chance to point at a
/// folder that has moved.
/// </remarks>
public static class LogFolder
{
    /// <inheritdoc cref="FileLoggerProvider.LogDirectory"/>
    public static string Path => FileLoggerProvider.LogDirectory;


    /// <summary>
    /// Shows the folder in Explorer. False where that failed, which leaves the caller to fall back
    /// to naming the path - the one thing this offers, and it not working is not worth an error
    /// dialog raised from inside an error dialog.
    /// </summary>
    public static bool TryOpen(ILogger? logger = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true });

            return true;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Could not open the log folder {Directory}.", Path);

            return false;
        }
    }
}
