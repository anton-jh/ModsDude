using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorInstanceSettings : DynamicForm<FarmingSimulatorInstanceSettings>
{
    // The installer has used both spellings over the years, and neither is guessable from the
    // other, so probe for whichever one is actually on disk.
    private static readonly string[] _gameDataFolderNames =
        ["FarmingSimulator2025", "Farming Simulator 2025"];


    public FarmingSimulatorInstanceSettings()
    {
        var myGames = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games");

        var gameDataFolder = _gameDataFolderNames
            .Select(x => Path.Join(myGames, x))
            .FirstOrDefault(Directory.Exists);

        if (gameDataFolder is not null)
        {
            GameDataFolder = new(gameDataFolder);
        }
    }


    [Required, CanBeModified, Title("Game data folder"), FolderPath]
    public string? GameDataFolder { get; set; }


    protected override IEnumerable<DynamicFormValidationError<FarmingSimulatorInstanceSettings>> PerformValidation()
    {
        if (!Directory.Exists(GameDataFolder))
        {
            yield return new("Folder does not exist.", nameof(GameDataFolder));
        }
    }


    public static FarmingSimulatorInstanceSettings Deserialize(string serialized)
    {
        return JsonSerializer.Deserialize<FarmingSimulatorInstanceSettings>(serialized)
            ?? throw new ArgumentException("Could not deserialize instance settings");
    }
}
