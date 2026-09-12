using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using System.Reflection;
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
    public GameIdentity Scope => new(Id.Id, BaseSettings.GameVersion switch
    {
        { } gameVersion => gameVersion.ToString().ToLowerInvariant(),
        null => throw new InvalidOperationException("Base settings without a game version cannot produce a game identity.")
    });

    /// <summary>
    /// The particular game rather than the adapter, so a sidebar grouping repos by game puts the FS22
    /// ones somewhere other than the FS25 ones - which is the same distinction <see cref="Scope"/>
    /// makes and has to agree with.
    /// </summary>
    /// <remarks>
    /// Read off the enum member's own <see cref="TitleAttribute"/>, which is what the base settings
    /// form already labels the choice with, so the heading reads as whatever the user picked when the
    /// repo was created. A version this adapter has no title for falls back to the adapter's name,
    /// which is a heading rather than a crash.
    /// </remarks>
    public string GameDisplayName => BaseSettings.GameVersion is FarmingSimulatorGameVersion version
        ? EnumTitle(version) ?? DisplayName
        : DisplayName;


    private static string? EnumTitle(FarmingSimulatorGameVersion version)
        => typeof(FarmingSimulatorGameVersion)
            .GetField(version.ToString())
            ?.GetCustomAttribute<TitleAttribute>()
            ?.Text;


    public DynamicForm DeserializeLocalSettings(string serializedLocalSettings)
    {
        var settings = JsonSerializer.Deserialize<FarmingSimulatorLocalSettings>(serializedLocalSettings)
            ?? throw new ArgumentException("Cannot deserialize local settings");

        settings.EnsureValid();

        return settings;
    }

    public Func<T>? GetBaseCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }

    public DynamicForm GetLocalSettingsTemplate()
    {
        return FarmingSimulatorLocalSettings.CreateTemplate(BaseSettings.GameVersion
            ?? throw new InvalidOperationException("Base settings without a game version cannot produce a local settings template."));
    }

    public ILocalGameAdapter WithLocalSettings(string serializedLocalSettings)
    {
        var localSettings = JsonSerializer.Deserialize<FarmingSimulatorLocalSettings>(serializedLocalSettings)
            ?? throw new ArgumentException("Could not deserialize local settings");
        localSettings.EnsureValid();

        return new FarmingSimulatorLocalGameAdapter(BaseSettings, localSettings, Loggers);
    }

    public ILocalGameAdapter WithLocalSettings(DynamicForm localSettings)
    {
        if (localSettings is not FarmingSimulatorLocalSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorLocalSettings>(localSettings);
        }
        return new FarmingSimulatorLocalGameAdapter(BaseSettings, settings, Loggers);
    }
}


public class FarmingSimulatorLocalGameAdapter(
    FarmingSimulatorBaseSettings baseSettings,
    FarmingSimulatorLocalSettings localSettings,
    ILoggerFactory? loggerFactory = null)
    : FarmingSimulatorBaseGameAdapter(baseSettings, loggerFactory), ILocalGameAdapter
{
    // Typed as Func<TCapability> rather than Func<object>, which is what the lookup matches on.
    private readonly List<object> _capabilities = [
        new Func<ILocalModAdapter>(() => new FarmingSimulatorLocalModAdapter(localSettings, loggerFactory)),
        new Func<ILocalSavegameAdapter>(() => new FarmingSimulatorLocalSavegameAdapter(localSettings, loggerFactory))
        ];


    public DynamicForm LocalSettings { get; } = localSettings;


    public Func<T>? GetLocalCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }
}
