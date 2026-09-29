using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Reflection;
using System.Text.Json;

namespace ModsDude.Client.Core.GameAdapters.Implementations.FarmingSimulatorV1;
public class FarmingSimulatorGameAdapter(ILoggerFactory? loggerFactory = null, IModHubClient? modHubClient = null) : IGameAdapter
{
    /// <summary>
    /// Handed down to the capability adapters, which read files somebody else wrote and degrade
    /// rather than throw when one will not parse - so without this the degrading is invisible.
    /// Optional, and null in a designer or a test that constructs an adapter directly.
    /// </summary>
    protected ILoggerFactory Loggers { get; } = loggerFactory ?? NullLoggerFactory.Instance;

    /// <summary>
    /// What ModHub is asked through - the ModsDude server's copy of it. Optional like
    /// <see cref="Loggers"/>; without it the game simply has no remote source.
    /// </summary>
    protected IModHubClient? ModHub { get; } = modHubClient;

    public GameAdapterId Id { get; } = new("_farming_simulator", 1);
    public string DisplayName { get; } = "Farming Simulator";
    public string ShortName { get; } = "FS";
    public string Description { get; } = "For Farming Simulator 25.";


    public DynamicForm GetBaseSettingsTemplate()
    {
        return new FarmingSimulatorBaseSettings();
    }

    public IBaseGameAdapter WithBaseSettings(string serializedBaseSettings)
    {
        var settings = JsonSerializer.Deserialize<FarmingSimulatorBaseSettings>(serializedBaseSettings)
            ?? throw new ArgumentException("Could not deserialize base settings");

        settings.EnsureValid();

        return new FarmingSimulatorBaseGameAdapter(settings, Loggers, ModHub);
    }

    public IBaseGameAdapter WithBaseSettings(DynamicForm baseSettings)
    {
        if (baseSettings is not FarmingSimulatorBaseSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorBaseSettings>(baseSettings);
        }

        settings.EnsureValid();

        return new FarmingSimulatorBaseGameAdapter(settings, Loggers, ModHub);
    }
}

public class FarmingSimulatorBaseGameAdapter(
    FarmingSimulatorBaseSettings settings,
    ILoggerFactory? loggerFactory = null,
    IModHubClient? modHubClient = null)
    : FarmingSimulatorGameAdapter(loggerFactory, modHubClient), IBaseGameAdapter
{
    // Instance rather than static, now that the adapters it builds are handed a logger: a static
    // list would close over whichever adapter happened to build it first and hand those loggers to
    // every other.
    private readonly List<object> _capabilities = [
        new Func<IBaseModAdapter>(() => new FarmingSimulatorBaseModAdapter(RequireGameVersion(settings), loggerFactory)),
        new Func<IBaseSavegameAdapter>(() => new FarmingSimulatorBaseSavegameAdapter(RequireGameVersion(settings), loggerFactory)),
        .. RemoteSources(settings, modHubClient)
        ];


    public FarmingSimulatorBaseSettings BaseSettings { get; } = settings;
    DynamicForm IBaseGameAdapter.BaseSettings => BaseSettings;

    public bool CanSupportMods { get; } = true;
    public bool CanSupportSavegames { get; } = true;

    /// <summary>
    /// One adapter can serve several games in the series, and their mod folders are not
    /// interchangeable sync targets, so the adapter id alone would offer one game's folder to
    /// another game's repo.
    /// </summary>
    public GameIdentity Scope => new(Id.Id, RequireGameVersion(BaseSettings).ToString().ToLowerInvariant());

    /// <summary>
    /// The particular game rather than the adapter, so a sidebar grouping repos by game puts each
    /// game in the series under a heading of its own - which is the same distinction
    /// <see cref="Scope"/> makes and has to agree with.
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

    /// <summary>
    /// What players call each game for short. Spelled out per version rather than worked out from the
    /// enum's number, which is the release year because the executables and folders are named by it -
    /// that it ends in the same digits as the name is a coincidence of this series.
    /// </summary>
    public string GameShortName => BaseSettings.GameVersion switch
    {
        FarmingSimulatorGameVersion.Fs25 => "FS25",
        _ => ShortName
    };

    /// <summary>
    /// The launcher, <c>FarmingSimulator2025.exe</c>, and the game it starts from <c>x64</c>,
    /// <c>FarmingSimulator2025Game.exe</c> - named after the year the enum is numbered by.
    /// </summary>
    public IReadOnlyList<string> ProcessNames => BaseSettings.GameVersion is FarmingSimulatorGameVersion version
        ? [$"FarmingSimulator{(int)version}", $"FarmingSimulator{(int)version}Game"]
        : [];


    /// <summary>
    /// ModHub, where the server crawls the repo's game - and nothing at all otherwise, so the capability
    /// is absent rather than empty for a game ModHub is not read for.
    /// </summary>
    private static IEnumerable<object> RemoteSources(FarmingSimulatorBaseSettings settings, IModHubClient? client)
    {
        if (client is null || FarmingSimulatorModHubSource.GameFor(settings.GameVersion) is not string game)
        {
            yield break;
        }

        IRemoteModSource[] sources = [new FarmingSimulatorModHubSource(client, game)];

        yield return new Func<IRemoteModSourcesAdapter>(() => new FarmingSimulatorRemoteModSourcesAdapter(sources));
    }

    protected static FarmingSimulatorGameVersion RequireGameVersion(FarmingSimulatorBaseSettings settings)
        => settings.GameVersion
            ?? throw new InvalidOperationException("Base settings without a game version cannot say which game they are for.");

    private static string? EnumTitle(FarmingSimulatorGameVersion version)
        => typeof(FarmingSimulatorGameVersion)
            .GetField(version.ToString())
            ?.GetCustomAttribute<TitleAttribute>()
            ?.Text;


    public DynamicForm DeserializeLocalSettings(string serializedLocalSettings)
    {
        return FarmingSimulatorLocalSettings.Deserialize(serializedLocalSettings);
    }

    public Func<T>? GetBaseCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }

    public DynamicForm GetLocalSettingsTemplate()
    {
        return new FarmingSimulatorLocalSettings();
    }

    public ILocalGameAdapter WithLocalSettings(string serializedLocalSettings)
    {
        return WithLocalSettings(FarmingSimulatorLocalSettings.Deserialize(serializedLocalSettings));
    }

    /// <exception cref="Exceptions.UserFriendlyException">
    /// The game has not made its data folder on this machine yet. Found here rather than when a
    /// capability is first asked for, so connecting a game that is not installed is refused outright.
    /// </exception>
    public ILocalGameAdapter WithLocalSettings(DynamicForm localSettings)
    {
        if (localSettings is not FarmingSimulatorLocalSettings settings)
        {
            throw new IncorrectGameAdapterSettingsTypeException<FarmingSimulatorLocalSettings>(localSettings);
        }

        var gameVersion = RequireGameVersion(BaseSettings);
        var gameDataFolder = FarmingSimulatorGameDataFolder.Require(gameVersion);

        return new FarmingSimulatorLocalGameAdapter(BaseSettings, settings, gameDataFolder, Loggers, ModHub);
    }
}


public class FarmingSimulatorLocalGameAdapter(
    FarmingSimulatorBaseSettings baseSettings,
    FarmingSimulatorLocalSettings localSettings,
    string gameDataFolder,
    ILoggerFactory? loggerFactory = null,
    IModHubClient? modHubClient = null)
    : FarmingSimulatorBaseGameAdapter(baseSettings, loggerFactory, modHubClient), ILocalGameAdapter
{
    // Typed as Func<TCapability> rather than Func<object>, which is what the lookup matches on.
    private readonly List<object> _capabilities = [
        new Func<ILocalModAdapter>(() => new FarmingSimulatorLocalModAdapter(RequireGameVersion(baseSettings), gameDataFolder, loggerFactory)),
        new Func<ILocalSavegameAdapter>(() => new FarmingSimulatorLocalSavegameAdapter(RequireGameVersion(baseSettings), gameDataFolder, loggerFactory))
        ];


    public DynamicForm LocalSettings { get; } = localSettings;


    public Func<T>? GetLocalCapabilityAdapterFactory<T>()
    {
        return _capabilities.OfType<Func<T>>().SingleOrDefault();
    }
}
