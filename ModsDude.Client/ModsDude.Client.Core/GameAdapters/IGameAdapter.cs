using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

public interface IGameAdapter
{
    GameAdapterId Id { get; }
    string DisplayName { get; }
    string Description { get; }

    /// <summary>
    /// How this game's version strings compare. An adapter that says nothing gets the shared parser,
    /// which covers dotted numerics with an optional v prefix and pre-release suffixes; a game
    /// numbering its mods by date or build number replaces it wholesale.
    /// </summary>
    /// <remarks>
    /// An overriding adapter is held to the same rule as the default one: <b>abstain rather than
    /// guess</b>. A version the comparer declines to place costs one question, asked once and stored
    /// repo-wide; a version it places wrongly is not noticed until a profile pins the wrong build.
    /// </remarks>
    IModVersionComparer VersionComparer => DefaultModVersionComparer.Instance;

    DynamicForm GetBaseSettingsTemplate();
    IBaseGameAdapter WithBaseSettings(string serializedBaseSettings);
    IBaseGameAdapter WithBaseSettings(DynamicForm baseSettings);
}

public interface IBaseGameAdapter : IGameAdapter
{
    DynamicForm BaseSettings { get; }
    bool CanSupportMods { get; }
    bool CanSupportSavegames { get; }

    /// <summary>
    /// The identity of the game these base settings configure the adapter for. An adapter serving
    /// one game says nothing and gets its id alone; one serving several overrides this from a base
    /// settings field, which must not be marked [CanBeModified] - see
    /// docs/04-game-adapters.md#instance-scope.
    /// </summary>
    /// <remarks>
    /// <see cref="GameAdapterId.Id"/> without the compatibility version, deliberately: a repo on
    /// '@2' still matches instances created under '@1', which is what compatibility versions exist
    /// for.
    /// </remarks>
    InstanceScope Scope => new(Id.Id);

    DynamicForm GetInstanceSettingsTemplate();
    DynamicForm DeserializeInstanceSettings(string serializedInstanceSettings);
    Func<T>? GetBaseCapabilityAdapterFactory<T>();
    IInstanceGameAdapter WithInstanceSettings(string serializedInstanceSettings);
    IInstanceGameAdapter WithInstanceSettings(DynamicForm instanceSettings);
}

public interface IInstanceGameAdapter : IBaseGameAdapter
{
    DynamicForm InstanceSettings { get; }

    Func<T>? GetInstanceCapabilityAdapterFactory<T>();
}

public interface IBaseModAdapter
{
    /// <summary>
    /// Whether this game's mod files are safe to hardlink into the content store.
    /// </summary>
    /// <remarks>
    /// False when the game or its updater may <b>rewrite a mod file in place</b>, which through a
    /// hardlink would corrupt the store blob shared with every other repo and instance on that
    /// volume. False also means "nobody has checked yet", which is why it is the default: the
    /// failure is silent, the blast radius is every repo on the disk, and a slow sync is visible and
    /// recoverable where a corrupted store is neither. Setting it true is an opt-in that means
    /// somebody tested this game's updater.
    /// See docs/07-mod-sync-design.md#hardlink-support-is-an-adapter-property.
    /// </remarks>
    bool SupportsHardlinks => false;

    Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken);
    IInstanceModAdapter WithInstanceSettings(string serializedInstanceSettings);
    IInstanceModAdapter WithInstanceSettings(DynamicForm instanceSettings);
}

public interface IInstanceModAdapter : IBaseModAdapter
{
    /// <summary>
    /// The mod folder this instance owns. No two instances may own the same one, whatever their
    /// scopes - scoping instances to a game rather than an adapter is what makes that possible.
    /// </summary>
    string ModFolder { get; }

    Task<IEnumerable<LocalMod>> GetInstalledMods(CancellationToken cancellationToken);

    /// <summary>
    /// Where a mod version's file belongs, and what it is called. The write side of the adapter -
    /// where a file goes is game knowledge, and nothing else can answer it.
    /// </summary>
    /// <remarks>
    /// A path rather than an <c>InstallMod</c> taking a stream, deliberately. Materialising is a
    /// hardlink on one disk and a copy on another, and that decision depends on the store assignment
    /// and the filesystem rather than on the game - so it belongs in the sync engine, once, instead
    /// of in every adapter. Adapters supply paths; the engine performs the filesystem operations.
    /// See docs/07-mod-sync-design.md#fitting-it-into-the-client.
    /// </remarks>
    string GetModFilePath(ModKey modId, ModVersionKey versionId);

    /// <summary>
    /// The file to remove when uninstalling a mod that is currently installed. The uninstall half of
    /// the same contract, separate from <see cref="GetModFilePath"/> because what is on disk is not
    /// necessarily where this adapter version would put it.
    /// </summary>
    string GetInstalledModPath(LocalMod installed) => installed.FilePath;
}

public interface IBaseSavegameAdapter
{
    /// <summary>
    /// Whether this game lets a save be put somewhere that does not exist yet. False for a game with
    /// a fixed set of numbered slots, true for one whose saves are named freely.
    /// </summary>
    /// <remarks>
    /// The point of asking this rather than a slot <em>count</em> is that both games are then the
    /// same model - an adapter-supplied list of containers, some occupied - and nothing outside an
    /// adapter ever hardcodes twenty.
    /// </remarks>
    bool CanCreateSlots { get; }

    IInstanceSavegameAdapter WithInstanceSettings(string serializedInstanceSettings);
    IInstanceSavegameAdapter WithInstanceSettings(DynamicForm instanceSettings);
}

/// <summary>
/// The write side for savegames, and - as with mods - deliberately only paths and facts. The engine
/// packs, unpacks, hashes and displaces; the adapter says where saves live, which of them exist, and
/// what belongs in one.
/// </summary>
public interface IInstanceSavegameAdapter : IBaseSavegameAdapter
{
    /// <summary>
    /// Every slot this instance has, occupied or not, in the order a picker should show them.
    /// </summary>
    /// <remarks>
    /// Reads each occupied slot far enough to name it, because a picker that says "savegame3" is the
    /// memory test this feature exists to remove. A slot whose contents cannot be read comes back
    /// occupied with a null name rather than being omitted or thrown over: it is still somebody's
    /// data and must still be impossible to overwrite by accident.
    /// </remarks>
    Task<IReadOnlyList<SavegameSlot>> GetSlots(CancellationToken cancellationToken);

    /// <summary>The folder a slot's contents live in. It need not exist yet.</summary>
    string GetSlotPath(SavegameSlotId slot);

    /// <summary>
    /// The slot a save called <paramref name="name"/> would occupy, for a game where
    /// <see cref="IBaseSavegameAdapter.CanCreateSlots"/> is true. Mints an address, not a folder -
    /// creating it is the engine's business.
    /// </summary>
    SavegameSlotId CreateSlot(string name) => throw new NotSupportedException("This game's savegames cannot be put in a slot that does not already exist.");

    /// <summary>
    /// Whether a file inside a slot's folder, given relative to it, is part of the save.
    /// </summary>
    /// <remarks>
    /// The exclusion list is game knowledge and belongs here: screenshots, thumbnails and caches are
    /// bulk that regenerates, and shipping them would make every check-in bigger and every content
    /// hash differ for reasons nobody played. Defaults to taking everything, which is the safe answer
    /// for an adapter that has not thought about it - a save that carries too much still restores.
    /// </remarks>
    bool BelongsInPackedSave(string relativePath) => true;
}
