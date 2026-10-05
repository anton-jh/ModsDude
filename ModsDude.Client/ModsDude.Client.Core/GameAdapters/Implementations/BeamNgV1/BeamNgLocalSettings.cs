using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.BeamNgV1;

/// <summary>
/// Which of the three folders this machine syncs. Each is optional: a blank one is a target the
/// game does not have here.
/// </summary>
public class BeamNgLocalSettings : DynamicForm<BeamNgLocalSettings>
{
    [CanBeModified, Title("Game mods folder"), FolderPath]
    public string? GameModsFolder { get; set; }

    [CanBeModified, Title("BeamMP server mods folder"), FolderPath]
    public string? ServerModsFolder { get; set; }

    [CanBeModified, Title("BeamMP launcher cache"), FolderPath]
    public string? LauncherCacheFolder { get; set; }


    public static BeamNgLocalSettings Deserialize(string serialized)
    {
        return JsonSerializer.Deserialize<BeamNgLocalSettings>(serialized)
            ?? throw new ArgumentException("Could not deserialize local settings");
    }

    /// <summary>
    /// A form filled in with whichever of the game's and the launcher's usual folders exist on this
    /// machine. The server's has no usual place, so it starts blank.
    /// </summary>
    public static BeamNgLocalSettings WithDetectedFolders(ILogger logger)
    {
        return new BeamNgLocalSettings
        {
            GameModsFolder = BeamNgFolders.FindGameModsFolder(),
            LauncherCacheFolder = BeamNgFolders.FindLauncherCacheFolder(logger)
        };
    }
}
