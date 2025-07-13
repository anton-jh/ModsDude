using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorInstanceSettings : DynamicForm<FarmingSimulatorInstanceSettings>
{
    public FarmingSimulatorInstanceSettings()
    {
        var gameDataFolder = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games",
            "Farming Simulator 2025");
        if (Directory.Exists(gameDataFolder))
        {
            GameDataFolder = gameDataFolder;
        }
    }


    [Required, CanBeModified, Title("Game data folder")]
    public string? GameDataFolder { get; set; }


    protected override IEnumerable<DynamicFormValidationError<FarmingSimulatorInstanceSettings>> Validate()
    {
        if (!Directory.Exists(GameDataFolder))
        {
            yield return new("Folder does not exist.", nameof(GameDataFolder));
        }
    }
}
