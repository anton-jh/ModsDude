using ModsDude.WindowsClient.Model.DynamicForms;
using ModsDude.WindowsClient.Model.Models.ValueTypes;

namespace ModsDude.WindowsClient.Model.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorInstanceConfig
{
    public FarmingSimulatorInstanceConfig()
    {
        var gameDataFolder = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games",
            "Farming Simulator 2025");
        if (Directory.Exists(gameDataFolder))
        {
            GameDataFolder = new DirectoryPath(gameDataFolder);
        }
    }


    [Required, CanBeModified, Label("Game data folder"), Description("Example: Documents/My Games/Farming Simulator 2025/")]
    public DirectoryPath GameDataFolder { get; set; }
}
