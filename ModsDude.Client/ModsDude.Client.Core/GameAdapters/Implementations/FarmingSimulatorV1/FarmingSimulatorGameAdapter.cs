using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorGameAdapter(ILoggerFactory? loggerFactory = null) : IGameAdapter
{
    /// <summary>
    /// Handed down to the capability adapters, which read files somebody else wrote and degrade
    /// rather than throw when one will not parse - so without this the degrading is invisible.
    /// Optional, and null in a designer or a test that constructs an adapter directly.
    /// </summary>
    protected ILoggerFactory Loggers { get; } = loggerFactory ?? NullLoggerFactory.Instance;

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

        return new FarmingSimulatorBaseGameAdapter(settings, Loggers);
    }

    public IBaseGameAdapter WithBaseSettings(DynamicForm baseSettings)
    {
        if (baseSettings is not FarmingSimulatorBaseSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorBaseSettings>(baseSettings);
        }

        settings.EnsureValid();

        return new FarmingSimulatorBaseGameAdapter(settings, Loggers);
    }
}

public class FarmingSimulatorBaseGameAdapter(
    FarmingSimulatorBaseSettings settings,
    ILoggerFactory? loggerFactory = null)
    : FarmingSimulatorGameAdapter(loggerFactory), IBaseGameAdapter
{
    // Instance rather than static, now that the adapters it builds are handed a logger: a static
    // list would close over whichever adapter happened to build it first and hand those loggers to
    // every other.
    private readonly List<object> _capabilities = [
        new Func<IBaseModAdapter>(() => new FarmingSimulatorBaseModAdapter(loggerFactory)),
        new Func<IBaseSavegameAdapter>(() => new FarmingSimulatorBaseSavegameAdapter(loggerFactory))
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

        return new FarmingSimulatorInstanceGameAdapter(BaseSettings, instanceSettings, Loggers);
    }

    public IInstanceGameAdapter WithInstanceSettings(DynamicForm instanceSettings)
    {
        if (instanceSettings is not FarmingSimulatorInstanceSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorInstanceSettings>(instanceSettings);
        }
        return new FarmingSimulatorInstanceGameAdapter(BaseSettings, settings, Loggers);
    }
}


public class FarmingSimulatorInstanceGameAdapter(
    FarmingSimulatorBaseSettings baseSettings,
    FarmingSimulatorInstanceSettings instanceSettings,
    ILoggerFactory? loggerFactory = null)
    : FarmingSimulatorBaseGameAdapter(baseSettings, loggerFactory), IInstanceGameAdapter
{
    // Typed as Func<TCapability> rather than Func<object>, which is what the lookup matches on.
    private readonly List<object> _capabilities = [
        new Func<IInstanceModAdapter>(() => new FarmingSimulatorInstanceModAdapter(instanceSettings, loggerFactory)),
        new Func<IInstanceSavegameAdapter>(() => new FarmingSimulatorInstanceSavegameAdapter(instanceSettings, loggerFactory))
        ];


    public DynamicForm InstanceSettings { get; } = instanceSettings;


    public Func<T>? GetInstanceCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }
}
