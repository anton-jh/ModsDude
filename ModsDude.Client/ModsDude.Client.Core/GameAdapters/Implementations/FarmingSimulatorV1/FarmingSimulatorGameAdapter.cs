using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorGameAdapter : IGameAdapter
{
    public GameAdapterId Id { get; } = new("_farming_simulator", 1);
    public string DisplayName { get; } = "Farming Simulator";
    public string Description { get; } = "For Farming Simulator 25.";
    public bool CanSupportMods { get; } = true;

    public bool CanSupportSavegames { get; } = true;


    public DynamicForm GetBaseSettingsTemplate()
    {
        return new FarmingSimulatorBaseSettings();
    }

    public IBaseGameAdapter WithBaseSettings(string serializedBaseSettings)
    {
        var settings = JsonSerializer.Deserialize<FarmingSimulatorBaseSettings>(serializedBaseSettings)
            ?? throw new ArgumentException("Could not deserialize base settings");

        settings.EnsureValid();

        return new FarmingSimulatorBaseGameAdapter(settings);
    }

    public IBaseGameAdapter WithBaseSettings(DynamicForm baseSettings)
    {
        if (baseSettings is not FarmingSimulatorBaseSettings settings)
        {
            throw new ArgumentException($"Incorrect base settings type. Expected '{typeof(FarmingSimulatorBaseSettings).FullName}', got '{baseSettings.GetType().FullName}'");
        }

        settings.EnsureValid();

        return new FarmingSimulatorBaseGameAdapter(settings);
    }
}

public class FarmingSimulatorBaseGameAdapter(
    FarmingSimulatorBaseSettings settings)
    : FarmingSimulatorGameAdapter, IBaseGameAdapter
{
    private static readonly List<Func<object>> _capabilities = [
        () => new FarmingSimulatorBaseModAdapter(),
        () => new FarmingSimulatorBaseSavegameAdapter()
        ];


    public FarmingSimulatorBaseSettings BaseSettings { get; } = settings;
    DynamicForm IBaseGameAdapter.BaseSettings => BaseSettings;


    public DynamicForm DeserializeInstanceSettings(string serializedInstanceSettings)
    {
        var settings = JsonSerializer.Deserialize<FarmingSimulatorInstanceSettings>(serializedInstanceSettings)
            ?? throw new ArgumentException("Cannot deserialize instance settings");

        settings.EnsureValid();

        return settings;
    }

    public Func<T>? GetBaseCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }

    public DynamicForm GetInstanceSettingsTemplate()
    {
        return new FarmingSimulatorInstanceSettings();
    }

    public IInstanceGameAdapter WithInstanceSettings(string serializedInstanceSettings)
    {
        var instanceSettings = JsonSerializer.Deserialize<FarmingSimulatorInstanceSettings>(serializedInstanceSettings)
            ?? throw new ArgumentException("Could not deserialize instance settings");

        return new FarmingSimulatorInstanceGameAdapter(BaseSettings, instanceSettings);
    }

    public IInstanceGameAdapter WithInstanceSettings(DynamicForm instanceSettings)
    {
        throw new NotImplementedException();
    }
}


public class FarmingSimulatorInstanceGameAdapter(
    FarmingSimulatorBaseSettings baseSettings,
    FarmingSimulatorInstanceSettings instanceSettings)
    : FarmingSimulatorBaseGameAdapter(baseSettings), IInstanceGameAdapter
{
    private readonly List<Func<object>> _capabilities = [
        () => new FarmingSimulatorInstanceModAdapter(instanceSettings),
        () => new FarmingSimulatorInstanceSavegameAdapter()
        ];


    public DynamicForm InstanceSettings { get; } = instanceSettings;


    public Func<T>? GetInstanceCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }
}
