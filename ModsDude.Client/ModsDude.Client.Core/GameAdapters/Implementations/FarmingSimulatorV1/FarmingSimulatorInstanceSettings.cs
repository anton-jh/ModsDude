using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorInstanceSettings : IAdapterInstanceSettings
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


    [Required, CanBeModified, Label("Game data folder"), Description("Example: Documents/My Games/Farming Simulator 2025/")]
    public string? GameDataFolder { get; set; }
}
