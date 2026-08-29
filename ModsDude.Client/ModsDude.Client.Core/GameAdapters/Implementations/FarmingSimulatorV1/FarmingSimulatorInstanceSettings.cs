using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorInstanceSettings : DynamicForm<FarmingSimulatorInstanceSettings>
{
    [Required, CanBeModified, Title("Game data folder"), FolderPath]
    public string? GameDataFolder { get; set; }


    protected override IEnumerable<DynamicFormValidationError<FarmingSimulatorInstanceSettings>> PerformValidation()
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
    public static FarmingSimulatorInstanceSettings CreateTemplate(FarmingSimulatorGameVersion gameVersion)
    {
        var myGames = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games");

        var year = (int)gameVersion;

        var gameDataFolder = new[] { $"FarmingSimulator{year}", $"Farming Simulator {year}" }
            .Select(x => Path.Join(myGames, x))
            .FirstOrDefault(Directory.Exists);

        return new FarmingSimulatorInstanceSettings()
        {
            GameDataFolder = gameDataFolder
        };
    }

    public static FarmingSimulatorInstanceSettings Deserialize(string serialized)
    {
        return JsonSerializer.Deserialize<FarmingSimulatorInstanceSettings>(serialized)
            ?? throw new ArgumentException("Could not deserialize instance settings");
    }
}
