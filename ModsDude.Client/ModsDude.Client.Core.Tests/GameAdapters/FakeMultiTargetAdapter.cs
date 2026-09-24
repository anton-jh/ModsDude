using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using System.Text.Json;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// A game with three optional targets, written before anything needs one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Farming Simulator has one target and the BeamNG adapter does not exist</b>, so without this
/// every multi-target path in Phase 10 would ship having never run - <see cref="ModTargets"/> would
/// be a list in the type system and a single folder everywhere else. This is what the rest of the
/// phase is developed against.
/// </para>
/// <para>
/// Three targets rather than a fake per shape: 0, 1, 2 and 3 are all reachable by filling in
/// different fields, and so are the three pairings - mods without savegames, savegames without mods,
/// and both - which is what the whole phase rests on.
/// </para>
/// </remarks>
internal class FakeMultiTargetGameAdapter : IGameAdapter
{
    public GameAdapterId Id { get; } = new("_fake_multi_target", 1);
    public string DisplayName => "Multi-target test game";
    public string Description => "Three optional targets, for exercising what Farming Simulator cannot.";


    public DynamicForm GetBaseSettingsTemplate() => new EmptyAdapterSettings();

    public IBaseGameAdapter WithBaseSettings(string serializedBaseSettings) => new FakeMultiTargetBaseGameAdapter();

    public IBaseGameAdapter WithBaseSettings(DynamicForm baseSettings) => new FakeMultiTargetBaseGameAdapter();


    /// <summary>The whole chain the app walks, from an adapter to the folders it reaches.</summary>
    public static ILocalModAdapter ModAdapterFor(FakeMultiTargetSettings settings)
    {
        return new FakeMultiTargetGameAdapter()
            .WithBaseSettings(new EmptyAdapterSettings())
            .WithLocalSettings(settings.Serialize())
            .GetLocalCapabilityAdapterFactory<ILocalModAdapter>()!
            .Invoke();
    }

    /// <summary>The same chain to the other half of a target - the folders saves live in.</summary>
    public static ILocalSavegameAdapter SavegameAdapterFor(FakeMultiTargetSettings settings)
    {
        return new FakeMultiTargetGameAdapter()
            .WithBaseSettings(new EmptyAdapterSettings())
            .WithLocalSettings(settings.Serialize())
            .GetLocalCapabilityAdapterFactory<ILocalSavegameAdapter>()!
            .Invoke();
    }
}


/// <summary>
/// Nothing in the base settings, deliberately: this adapter serves one game, so its
/// <see cref="GameIdentity"/> is its id alone and every target comes from the local settings.
/// </summary>
internal class FakeMultiTargetBaseGameAdapter : FakeMultiTargetGameAdapter, IBaseGameAdapter
{
    private readonly List<object> _capabilities = [
        new Func<IBaseModAdapter>(() => new FakeMultiTargetBaseModAdapter()),
        new Func<IBaseSavegameAdapter>(() => new FakeMultiTargetBaseSavegameAdapter())
        ];


    public DynamicForm BaseSettings { get; } = new EmptyAdapterSettings();

    public bool CanSupportMods => true;
    public bool CanSupportSavegames => true;


    public DynamicForm GetLocalSettingsTemplate() => new FakeMultiTargetSettings();

    public DynamicForm DeserializeLocalSettings(string serializedLocalSettings)
        => FakeMultiTargetSettings.Deserialize(serializedLocalSettings);

    public Func<T>? GetBaseCapabilityAdapterFactory<T>() => _capabilities.OfType<Func<T>>().SingleOrDefault();

    public ILocalGameAdapter WithLocalSettings(string serializedLocalSettings)
        => new FakeMultiTargetLocalGameAdapter(FakeMultiTargetSettings.Deserialize(serializedLocalSettings));

    public ILocalGameAdapter WithLocalSettings(DynamicForm localSettings)
        => localSettings is FakeMultiTargetSettings settings
            ? new FakeMultiTargetLocalGameAdapter(settings)
            : throw new IncorrectGameAdapterSettingsTypeException<FakeMultiTargetSettings>(localSettings);
}


internal sealed class FakeMultiTargetLocalGameAdapter(FakeMultiTargetSettings settings)
    : FakeMultiTargetBaseGameAdapter, ILocalGameAdapter
{
    private readonly List<object> _capabilities = [
        new Func<ILocalModAdapter>(() => new FakeMultiTargetModAdapter(settings)),
        new Func<ILocalSavegameAdapter>(() => new FakeMultiTargetSavegameAdapter(settings))
        ];


    public DynamicForm LocalSettings { get; } = settings;

    /// <summary>
    /// The pairing, as the settings hold it. The two capability adapters each answer with their own
    /// half of it under the same keys, which is the whole of how a save is tied to the mods it was
    /// played against; this is where a test reads both halves at once.
    /// </summary>
    public IReadOnlyList<FakeTarget> Targets => settings.Targets;


    public Func<T>? GetLocalCapabilityAdapterFactory<T>() => _capabilities.OfType<Func<T>>().SingleOrDefault();
}


internal class FakeMultiTargetBaseModAdapter : IBaseModAdapter
{
    public bool SupportsHardlinks => true;


    /// <summary>
    /// Every zip in the folder is a mod, named after the file and versioned by whatever is written
    /// inside it. Enough for a sync to plan against, and nothing more.
    /// </summary>
    public Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path) is false)
        {
            return Task.FromResult<IEnumerable<LocalMod>>([]);
        }

        return Task.FromResult(Directory.EnumerateFiles(path, "*.zip").Select(file =>
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var written = File.ReadAllText(file).Trim();

            return new LocalMod(
                ModKey.From(name),
                ModVersionKey.From(written.Length > 0 ? written : "1"),
                name,
                "",
                () => File.OpenRead(file))
            {
                FilePath = file,
                FileLength = new FileInfo(file).Length
            };
        }));
    }

    public ILocalModAdapter WithLocalSettings(string serializedLocalSettings)
        => new FakeMultiTargetModAdapter(FakeMultiTargetSettings.Deserialize(serializedLocalSettings));

    public ILocalModAdapter WithLocalSettings(DynamicForm localSettings)
        => localSettings is FakeMultiTargetSettings settings
            ? new FakeMultiTargetModAdapter(settings)
            : throw new IncorrectGameAdapterSettingsTypeException<FakeMultiTargetSettings>(localSettings);
}


internal sealed class FakeMultiTargetModAdapter(FakeMultiTargetSettings settings)
    : FakeMultiTargetBaseModAdapter, ILocalModAdapter
{
    /// <summary>
    /// Only the targets with a mod folder. A target configured with savegames and nothing else is an
    /// ordinary target that sync has no business in, rather than one with a null path.
    /// </summary>
    public ModTargets ModTargets => new(settings.Targets
        .Where(x => x.ModFolder is not null)
        .Select(x => new ModTarget(x.Key, x.DisplayName, x.ModFolder!)));


    public async Task<IEnumerable<LocalMod>> GetInstalledMods(ModTarget target, Func<string, bool> skip, CancellationToken cancellationToken)
        => (await GetModsFromFolder(target.Path, cancellationToken)).Where(x => skip(x.FilePath) is false);

    public string GetModFilePath(ModTarget target, ModKey modId, ModVersionKey versionId, ModFileName? fileName)
        => Path.Combine(target.Path, fileName?.Value ?? $"{modId.Value}.zip");
}


/// <summary>
/// The savegame half of the fake, in the shape the real BeamNG adapter's will be: the folders come
/// out of the same settings the mod folders do, under the same keys.
/// </summary>
/// <remarks>
/// Slots are folders under a target's savegame folder, occupied when they have anything in them -
/// which is what a real adapter decides by reading the save. Enough for a check-out to write into
/// and a drift check to hash, and nothing more.
/// </remarks>
internal class FakeMultiTargetBaseSavegameAdapter : IBaseSavegameAdapter
{
    /// <summary>Named freely, so a test can mint a slot rather than picking from twenty.</summary>
    public bool CanCreateSlots => true;


    public ILocalSavegameAdapter WithLocalSettings(string serializedLocalSettings)
        => new FakeMultiTargetSavegameAdapter(FakeMultiTargetSettings.Deserialize(serializedLocalSettings));

    public ILocalSavegameAdapter WithLocalSettings(DynamicForm localSettings)
        => localSettings is FakeMultiTargetSettings settings
            ? new FakeMultiTargetSavegameAdapter(settings)
            : throw new IncorrectGameAdapterSettingsTypeException<FakeMultiTargetSettings>(localSettings);
}


internal sealed class FakeMultiTargetSavegameAdapter(FakeMultiTargetSettings settings)
    : FakeMultiTargetBaseSavegameAdapter, ILocalSavegameAdapter
{
    /// <summary>
    /// Only the targets with a savegame folder. A target configured with mods and nothing else is an
    /// ordinary target with no saves in it, rather than one with a null path.
    /// </summary>
    public SavegameTargets SavegameTargets => new(settings.Targets
        .Where(x => x.SavegameFolder is not null)
        .Select(x => new SavegameTarget(x.Key, x.DisplayName, x.SavegameFolder!)));


    public string GetSlotPath(SavegameTarget target, SavegameSlotId slot) => Path.Combine(target.Path, slot.Value);

    public SavegameSlotId CreateSlot(string name) => new(name);

    public Task<IReadOnlyList<SavegameSlot>> GetSlots(SavegameTarget target, CancellationToken cancellationToken)
    {
        if (Directory.Exists(target.Path) is false)
        {
            return Task.FromResult<IReadOnlyList<SavegameSlot>>([]);
        }

        IReadOnlyList<SavegameSlot> slots =
        [
            .. Directory.EnumerateDirectories(target.Path)
                .Select(x => new SavegameSlot(
                    new SavegameSlotId(Path.GetFileName(x)),
                    Path.GetFileName(x),
                    Directory.EnumerateFileSystemEntries(x).Any(),
                    []))
        ];

        return Task.FromResult(slots);
    }
}


/// <param name="ModFolder">
/// Null for a target that holds savegames and no mods - the MP client whose mods the server's copy
/// serves, say.
/// </param>
/// <param name="SavegameFolder">Null for a target that is only somewhere mods go.</param>
internal sealed record FakeTarget(TargetKey Key, string DisplayName, string? ModFolder, string? SavegameFolder);


/// <summary>
/// Three targets, each of them two independently optional fields, which is how the BeamNG adapter's
/// will be shaped.
/// </summary>
/// <remarks>
/// <b>Nothing here is persisted as a target.</b> Emptying a field takes a target away and filling it
/// in puts it back, which is why the transitions are worth covering: the manifest and the savegame
/// binding behind a target that stopped existing are two very different problems, and an adapter
/// author renaming a key looks exactly like a user emptying its field.
/// </remarks>
internal sealed class FakeMultiTargetSettings : DynamicForm<FakeMultiTargetSettings>
{
    /// <summary>
    /// Written down because they are a contract rather than an implementation detail. A manifest and
    /// a savegame binding are keyed on these, so changing one in a later adapter version orphans both
    /// on every member's machine, and nothing can tell that from the target having been removed.
    /// </summary>
    public static TargetKey Server { get; } = new("server");
    public static TargetKey Client { get; } = new("client");
    public static TargetKey Solo { get; } = new("solo");


    [CanBeModified, Title("Dedicated server mods"), FolderPath]
    public string? ServerModFolder { get; set; }

    [CanBeModified, Title("Dedicated server savegames"), FolderPath]
    public string? ServerSavegameFolder { get; set; }

    [CanBeModified, Title("Multiplayer client mods"), FolderPath]
    public string? ClientModFolder { get; set; }

    [CanBeModified, Title("Multiplayer client savegames"), FolderPath]
    public string? ClientSavegameFolder { get; set; }

    [CanBeModified, Title("Singleplayer mods"), FolderPath]
    public string? SoloModFolder { get; set; }

    [CanBeModified, Title("Singleplayer savegames"), FolderPath]
    public string? SoloSavegameFolder { get; set; }


    /// <summary>
    /// A target for every pairing with something in it. Both fields blank is not a target carrying
    /// two nulls; it is a folder nobody connected.
    /// </summary>
    public IReadOnlyList<FakeTarget> Targets =>
    [
        .. new FakeTarget[]
        {
            new(Server, "Dedicated server", Blank(ServerModFolder), Blank(ServerSavegameFolder)),
            new(Client, "MP client", Blank(ClientModFolder), Blank(ClientSavegameFolder)),
            new(Solo, "Singleplayer", Blank(SoloModFolder), Blank(SoloSavegameFolder))
        }.Where(x => x.ModFolder is not null || x.SavegameFolder is not null)
    ];


    public static FakeMultiTargetSettings Deserialize(string serialized)
    {
        return JsonSerializer.Deserialize<FakeMultiTargetSettings>(serialized)
            ?? throw new ArgumentException("Could not deserialize local settings");
    }


    /// <summary>A field somebody cleared and one they never filled in are the same thing.</summary>
    private static string? Blank(string? folder) => string.IsNullOrWhiteSpace(folder) ? null : folder;
}
