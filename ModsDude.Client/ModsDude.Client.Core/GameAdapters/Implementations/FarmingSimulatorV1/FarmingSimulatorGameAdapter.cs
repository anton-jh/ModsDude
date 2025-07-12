namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorGameAdapter : GameAdapterBase<FarmingSimulatorBaseSettings, FarmingSimulatorInstanceSettings>
{
    public override GameAdapterDescriptor Descriptor { get; } = new(
        Id: new("_farming_simulator", "1"),
        DisplayName: "Farming Simulator",
        CompatibleWithGames: ["Farming Simulator 25"],
        Description: "For Farming Simulator 25.");

    public override IModAdapter? ModAdapter { get; } = new FarmingSimulatorModAdapter();

    public override ISavegameAdapter? SavegameAdapter { get; } = new FarmingSimulatorSavegameAdapter();
}
