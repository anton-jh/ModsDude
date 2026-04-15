using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;

public class FarmingSimulatorBaseSavegameAdapter : IBaseSavegameAdapter
{
    public IInstanceSavegameAdapter WithInstanceSettings(string serializedInstanceSettings)
    {
        var settings = FarmingSimulatorInstanceSettings.Deserialize(serializedInstanceSettings);
        settings.EnsureValid();
        return new FarmingSimulatorInstanceSavegameAdapter();
    }

    public IInstanceSavegameAdapter WithInstanceSettings(DynamicForm instanceSettings)
    {
        if (instanceSettings is not FarmingSimulatorInstanceSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorInstanceSettings>(instanceSettings);
        }
        settings.EnsureValid();
        return new FarmingSimulatorInstanceSavegameAdapter();
    }
}

public class FarmingSimulatorInstanceSavegameAdapter
    : FarmingSimulatorBaseSavegameAdapter, IInstanceSavegameAdapter
{

}
