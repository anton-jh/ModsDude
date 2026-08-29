using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorGameAdapter : IGameAdapter
{
    public GameAdapterId Id { get; } = new("_farming_simulator", 1);
    public string DisplayName { get; } = "Farming Simulator";
    public string Description { get; } = "For Farming Simulator 22 and 25.";


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
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorBaseSettings>(baseSettings);
        }

        settings.EnsureValid();

        return new FarmingSimulatorBaseGameAdapter(settings);
    }
}

public class FarmingSimulatorBaseGameAdapter(
    FarmingSimulatorBaseSettings settings)
    : FarmingSimulatorGameAdapter, IBaseGameAdapter
{
    private static readonly List<object> _capabilities = [
        new Func<IBaseModAdapter>(() => new FarmingSimulatorBaseModAdapter()),
        new Func<IBaseSavegameAdapter>(() => new FarmingSimulatorBaseSavegameAdapter())
        ];


    public FarmingSimulatorBaseSettings BaseSettings { get; } = settings;
    DynamicForm IBaseGameAdapter.BaseSettings => BaseSettings;

    public bool CanSupportMods { get; } = true;
    public bool CanSupportSavegames { get; } = true;

    /// <summary>
    /// One adapter serves both Farming Simulator 22 and 25, and their mod folders are not
    /// interchangeable sync targets, so the adapter id alone would offer an FS22 folder to an FS25
    /// repo.
    /// </summary>
    public InstanceScope Scope => new(Id.Id, BaseSettings.GameVersion switch
    {
        { } gameVersion => gameVersion.ToString().ToLowerInvariant(),
        null => throw new InvalidOperationException("Base settings without a game version cannot produce an instance scope.")
    });


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
        return FarmingSimulatorInstanceSettings.CreateTemplate(BaseSettings.GameVersion
            ?? throw new InvalidOperationException("Base settings without a game version cannot produce an instance settings template."));
    }

    public IInstanceGameAdapter WithInstanceSettings(string serializedInstanceSettings)
    {
        var instanceSettings = JsonSerializer.Deserialize<FarmingSimulatorInstanceSettings>(serializedInstanceSettings)
            ?? throw new ArgumentException("Could not deserialize instance settings");
        instanceSettings.EnsureValid();

        return new FarmingSimulatorInstanceGameAdapter(BaseSettings, instanceSettings);
    }

    public IInstanceGameAdapter WithInstanceSettings(DynamicForm instanceSettings)
    {
        if (instanceSettings is not FarmingSimulatorInstanceSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorInstanceSettings>(instanceSettings);
        }
        return new FarmingSimulatorInstanceGameAdapter(BaseSettings, settings);
    }
}


public class FarmingSimulatorInstanceGameAdapter(
    FarmingSimulatorBaseSettings baseSettings,
    FarmingSimulatorInstanceSettings instanceSettings)
    : FarmingSimulatorBaseGameAdapter(baseSettings), IInstanceGameAdapter
{
    // Typed as Func<TCapability> rather than Func<object>, which is what the lookup matches on.
    private readonly List<object> _capabilities = [
        new Func<IInstanceModAdapter>(() => new FarmingSimulatorInstanceModAdapter(instanceSettings)),
        new Func<IInstanceSavegameAdapter>(() => new FarmingSimulatorInstanceSavegameAdapter())
        ];


    public DynamicForm InstanceSettings { get; } = instanceSettings;


    public Func<T>? GetInstanceCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }
}
