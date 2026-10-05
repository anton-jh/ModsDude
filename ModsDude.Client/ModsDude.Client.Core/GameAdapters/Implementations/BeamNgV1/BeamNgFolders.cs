using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// Where BeamNG.drive and the BeamMP launcher keep mods on a machine that has not been told
/// otherwise - for filling in the settings form, never for syncing to without asking.
/// </summary>
public static class BeamNgFolders
{
    private const string _launcherConfigFile = "Launcher.cfg";
    private const string _defaultCachingDirectory = "Resources";


    public static string? FindGameModsFolder()
        => FindGameModsFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string? FindLauncherCacheFolder(ILogger logger)
        => FindLauncherCacheFolder(
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeamMP-Launcher"),
            logger);


    /// <summary>The <c>mods</c> folder of the game's user folder, where it has made one.</summary>
    internal static string? FindGameModsFolder(string localAppData)
    {
        var folder = Path.Join(localAppData, "BeamNG", "BeamNG.drive", "current", "mods");

        return Directory.Exists(folder) ? folder : null;
    }

    /// <summary>
    /// The launcher's cache: <c>CachingDirectory</c> from its <c>Launcher.cfg</c>, resolved against
    /// the launcher's own folder, or <c>Resources</c> beside it where the setting is absent.
    /// </summary>
    /// <remarks>
    /// A config that will not parse is reported and read as absent. This only fills in a form the user
    /// then looks at, so a wrong guess costs a click, where refusing would cost the whole form.
    /// </remarks>
    internal static string? FindLauncherCacheFolder(string launcherFolder, ILogger logger)
    {
        if (Directory.Exists(launcherFolder) is false)
        {
            return null;
        }

        var configured = ReadCachingDirectory(Path.Join(launcherFolder, _launcherConfigFile), logger);
        var folder = Path.GetFullPath(configured ?? _defaultCachingDirectory, launcherFolder);

        return Directory.Exists(folder) ? folder : null;
    }


    private static string? ReadCachingDirectory(string configFile, ILogger logger)
    {
        if (File.Exists(configFile) is false)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(configFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);

            return document.RootElement.ValueKind is JsonValueKind.Object &&
                document.RootElement.TryGetProperty("CachingDirectory", out var value) &&
                value.ValueKind is JsonValueKind.String &&
                string.IsNullOrWhiteSpace(value.GetString()) is false
                    ? value.GetString()
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(exception, "Could not read the BeamMP launcher's settings at {File}; assuming its default cache folder.", configFile);

            return null;
        }
    }
}
