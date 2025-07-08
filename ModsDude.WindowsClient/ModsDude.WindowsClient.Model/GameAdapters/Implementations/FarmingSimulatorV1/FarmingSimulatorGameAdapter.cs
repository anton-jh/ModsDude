
namespace ModsDude.WindowsClient.Model.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorGameAdapter : GameAdapterBase<FarmingSimulatorBaseConfig, FarmingSimulatorInstanceConfig>
{
    public override GameAdapterDescriptor Descriptor { get; } = new(
        Id: new("_farming_simulator", "1"),
        DisplayName: "Farming Simulator",
        CompatibleWithGames: ["Farming Simulator 25"],
        Description: "For Farming Simulator 25.");

    public override IModAdapter<FarmingSimulatorBaseConfig, FarmingSimulatorInstanceConfig>? ModAdapter { get; } = new FarmingSimulatorModAdapter();

    public override ISavegameAdapter<FarmingSimulatorBaseConfig, FarmingSimulatorInstanceConfig>? SavegameAdapter { get; } = new FarmingSimulatorSavegameAdapter();

    public override object GetBaseConfigurationTemplate()
        => new FarmingSimulatorInstanceConfig();

    public override object GetInstanceConfigurationTemplate()
        => new FarmingSimulatorInstanceConfig();
}
