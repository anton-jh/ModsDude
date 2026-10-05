using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

public interface IGameAdapter
{
    GameAdapterId Id { get; }
    string DisplayName { get; }

    /// <summary>
    /// <see cref="DisplayName"/> in a handful of characters, for where there is no room for the whole
    /// of it - the heading over a game's repos while the sidebar is a rail. The abbreviation players
    /// already use for the game, rather than a truncation of its name.
    /// </summary>
    string ShortName { get; }

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
    /// The <em>game</em> these base settings configure the adapter for, named for a person. An
    /// adapter serving one game says nothing and gets its own <see cref="IGameAdapter.DisplayName"/>;
    /// one serving several names the particular game, exactly as <see cref="Scope"/> identifies it.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="Scope"/> and it must agree with it: the sidebar groups repos by
    /// this, and two repos in one group whose games are not interchangeable would be the group
    /// heading telling a lie. One adapter can serve several games - Farming Simulator's is built to
    /// serve each game in the series - which is the whole reason this is not just
    /// <see cref="IGameAdapter.DisplayName"/>.
    /// </remarks>
    string GameDisplayName => DisplayName;

    /// <summary>
    /// <see cref="GameDisplayName"/> in a handful of characters, as <see cref="IGameAdapter.ShortName"/>
    /// is to <see cref="IGameAdapter.DisplayName"/>. An adapter serving several games names the
    /// particular one here too, or two groups the open sidebar tells apart would read the same in a rail.
    /// </summary>
    string GameShortName => ShortName;

    /// <summary>
    /// The identity of the game these base settings configure the adapter for. An adapter serving
    /// one game says nothing and gets its id alone; one serving several overrides this from a base
    /// settings field, which must not be marked [CanBeModified] - see
    /// docs/04-game-adapters.md#game-identity.
    /// </summary>
    /// <remarks>
    /// <see cref="GameAdapterId.Id"/> without the compatibility version, deliberately: a repo on
    /// '@2' still matches games created under '@1', which is what compatibility versions exist
    /// for.
    /// </remarks>
    GameIdentity Scope => new(Id.Id);

    /// <summary>
    /// The names of the processes that are this game running, without <c>.exe</c> - any one of them
    /// alive is the game being played.
    /// </summary>
    /// <remarks>
    /// What refuses any change to the game's files while it runs, and what lets the client notice
    /// somebody stopping play, which is the moment a checked-out savegame is worth a reminder to check
    /// it in. Every process a launch passes through belongs here - a launcher that stays up beside the
    /// game as well as the game itself. Empty, the default, is a game whose sessions nobody can see:
    /// nothing is refused or reminded of for it.
    /// </remarks>
    IReadOnlyList<string> ProcessNames => [];

    DynamicForm GetLocalSettingsTemplate();
    DynamicForm DeserializeLocalSettings(string serializedLocalSettings);
    Func<T>? GetBaseCapabilityAdapterFactory<T>();
    ILocalGameAdapter WithLocalSettings(string serializedLocalSettings);
    ILocalGameAdapter WithLocalSettings(DynamicForm localSettings);
}

public interface ILocalGameAdapter : IBaseGameAdapter
{
    DynamicForm LocalSettings { get; }

    Func<T>? GetLocalCapabilityAdapterFactory<T>();
}

public interface IBaseModAdapter
{
    /// <summary>
    /// Every attribute key this adapter can put on a <see cref="LocalMod"/>, for a search box to
    /// offer. On the base adapter rather than the local one, because a version only the repo holds
    /// carries attributes too and a reader with no game connected still searches them.
    /// </summary>
    /// <remarks>
    /// <b>The adapter reports nothing it has not declared here.</b> Nothing downstream checks, so
    /// keeping the two in step is the adapter's job. Empty, the default, is a game with no
    /// attributes at all, which every search still handles.
    /// </remarks>
    IReadOnlyList<ModAttributeDefinition> Attributes => [];

    SavegameCompatibilityPolicy SavegameCompatibility { get; }

    Task<IEnumerable<LocalMod>> GetModsFromFolder(string path, CancellationToken cancellationToken);
    ILocalModAdapter WithLocalSettings(string serializedLocalSettings);
    ILocalModAdapter WithLocalSettings(DynamicForm localSettings);
}

public interface ILocalModAdapter : IBaseModAdapter
{
    /// <summary>
    /// Every mod folder this game reaches on this machine, keyed. Almost every game answers with one
    /// and never names it; a game whose folders are separately configured - a dedicated server and
    /// the client that has to match it - answers with as many as its settings fill in.
    /// </summary>
    /// <remarks>
    /// <b>Derived from the local settings, every time.</b> Nothing persists a target, so emptying a
    /// settings field takes one away and filling it in puts it back, and a game reaching no folder at
    /// all is an ordinary answer rather than an error.
    /// </remarks>
    ModTargets ModTargets { get; }

    /// <param name="skip">
    /// Files the caller already knows, by full path - left unopened and left out of the answer. Sync
    /// passes the ones its manifest still describes, so planning opens only what has changed since
    /// the last apply rather than every archive in the folder.
    /// </param>
    Task<IEnumerable<LocalMod>> GetInstalledMods(ModTarget target, Func<string, bool> skip, CancellationToken cancellationToken);

    /// <summary>
    /// How a mod folder holds a set of mods: the file each one gets, and what else the game needs
    /// changed to match. The engine does every write.
    /// </summary>
    ModLayout Layout(ModLayoutContext context);
}

public interface IBaseSavegameAdapter
{
    ILocalSavegameAdapter WithLocalSettings(string serializedLocalSettings);
    ILocalSavegameAdapter WithLocalSettings(DynamicForm localSettings);
}

/// <summary>
/// The write side for savegames, and - as with mods - deliberately only paths and facts. The engine
/// packs, unpacks, hashes and displaces; the adapter says where saves live, which of them exist, and
/// what belongs in one.
/// </summary>
public interface ILocalSavegameAdapter : IBaseSavegameAdapter
{
    /// <summary>
    /// Every savegame folder this game reaches on this machine, keyed the same way its mod folders
    /// are. Almost every game answers with one and never names it.
    /// </summary>
    /// <remarks>
    /// <b>Derived from the local settings, every time</b>, exactly as <see cref="ILocalModAdapter.ModTargets"/>
    /// is. A key that appears here and not there is a target that holds saves and no mods, which is
    /// ordinary; a key that appears in neither is a target that no longer exists, and what happens to
    /// a savegame held behind one is <c>SavegameBindingStore</c>'s business rather than an adapter's.
    /// </remarks>
    SavegameTargets SavegameTargets { get; }

    /// <summary>
    /// Every slot one of this game's savegame folders has, occupied or not, in the order a picker
    /// should show them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads each occupied slot far enough to name it, because a picker that says "savegame3" is the
    /// memory test this feature exists to remove. A slot whose contents cannot be read comes back
    /// occupied with a null name rather than being omitted or thrown over: it is still somebody's
    /// data and must still be impossible to overwrite by accident.
    /// </para>
    /// <para>
    /// <b>Ids need only be unique within the target they came from</b>, which an adapter cannot get
    /// wrong: the engine pairs each one with the key it asked about, and
    /// <see cref="Models.SavegameSlotRef"/> is what addresses a place across a whole game. Asking
    /// instead for ids unique across every folder would put two targets' slots on one binding the
    /// first time somebody numbered from one twice.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SavegameSlot>> GetSlots(SavegameTarget target, CancellationToken cancellationToken);

    /// <summary>The folder a slot's contents live in. It need not exist yet.</summary>
    string GetSlotPath(SavegameTarget target, SavegameSlotId slot);

    /// <summary>
    /// The number a player knows this slot by, for a game whose slots are a fixed, numbered set - or null.
    /// </summary>
    /// <remarks>
    /// Answerable from the id alone, which is why it is not only a field of <see cref="SavegameSlot"/>: a
    /// hold recorded on this machine keeps the slot's address and nothing else, and the list of saves
    /// has to name the slot it is in without reading twenty folders to find out. Whatever this returns
    /// for an id is what <see cref="GetSlots"/> puts in <see cref="SavegameSlot.Number"/> for it.
    /// </remarks>
    int? GetSlotNumber(SavegameSlotId slot) => null;

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

    /// <summary>
    /// The edits, relative to the slot's folder, that make this game itself show the save as
    /// <see cref="SavegameRenameContext.Name"/>. Empty for a game that records no name.
    /// </summary>
    /// <remarks>
    /// Applied to the slot, not only to the archive being packed, so the game's own menu and the repo
    /// agree on what a save is called.
    /// </remarks>
    IReadOnlyList<GameFileEdit> RenameSavegame(SavegameRenameContext context) => [];
}

public sealed record SavegameRenameContext(SavegameTarget Target, SavegameSlotId Slot, string Name);
