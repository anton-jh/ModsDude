using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorLocalSettings : DynamicForm<FarmingSimulatorLocalSettings>
{
    [Required, CanBeModified, Title("Game data folder"), FolderPath]
    public string? GameDataFolder { get; set; }


    protected override IEnumerable<DynamicFormValidationError<FarmingSimulatorLocalSettings>> PerformValidation()
    {
        if (!Directory.Exists(GameDataFolder))
        {
            yield return new("Folder does not exist.", nameof(GameDataFolder));
        }
    }


    /// <summary>
    /// A blank form with the game data folder probed for the year the repo's base settings target.
    /// The installer has used both spellings over the years, and neither is guessable from the
    /// other, so probe for whichever one is actually on disk.
    /// </summary>
    public static FarmingSimulatorLocalSettings CreateTemplate(FarmingSimulatorGameVersion gameVersion)
    {
        var myGames = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games");

        var year = (int)gameVersion;

        var gameDataFolder = new[] { $"FarmingSimulator{year}", $"Farming Simulator {year}" }
            .Select(x => Path.Join(myGames, x))
            .FirstOrDefault(Directory.Exists);

        return new FarmingSimulatorLocalSettings()
        {
            GameDataFolder = gameDataFolder
        };
    }

    public static FarmingSimulatorLocalSettings Deserialize(string serialized)
    {
        return JsonSerializer.Deserialize<FarmingSimulatorLocalSettings>(serialized)
            ?? throw new ArgumentException("Could not deserialize local settings");
    }
}
