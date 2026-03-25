using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public record FarmingSimulatorInstanceSettings : DynamicForm<FarmingSimulatorInstanceSettings>
{
    public FarmingSimulatorInstanceSettings()
    {
        var gameDataFolder = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games",
            "Farming Simulator 2025");
        if (Directory.Exists(gameDataFolder))
        {
            GameDataFolder = new(gameDataFolder);
        }
    }


    [Required, CanBeModified, Title("Game data folder")]
    public FolderPath? GameDataFolder { get; set; }


    protected override IEnumerable<DynamicFormValidationError<FarmingSimulatorInstanceSettings>> PerformValidation()
    {
        if (!Directory.Exists(GameDataFolder?.Value))
        {
            yield return new("Folder does not exist.", nameof(GameDataFolder));
        }
    }
}
