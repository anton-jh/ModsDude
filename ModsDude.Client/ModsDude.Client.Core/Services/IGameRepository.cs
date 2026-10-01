using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Sync;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public interface IGameRepository : IModFolders, IDriftCandidateSource
{
    /// <summary>Every game configured on this machine.</summary>
    ObservableCollection<Game> Games { get; }

    /// <summary>
    /// Raised after any change to a game that is not an add or a remove.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The half <see cref="Games"/> cannot report.</b> Adding and deleting raise
    /// <c>CollectionChanged</c>, so a listener already hears about those; repointing a game at a
    /// different mod folder, or at a different profile, changes nothing about the collection and used
    /// to be silent. Both of those change whether the folder matches what was applied to it, which is
    /// the entire question the drift check answers - see
    /// docs/07-mod-sync-design.md#it-has-to-be-unmissable-everywhere.
    /// </para>
    /// <para>
    /// Raised after the state has been written, so a listener that reads the game back gets what
    /// was saved rather than what is about to be.
    /// </para>
    /// </remarks>
    event EventHandler? GameChanged;

    /// <summary>
    /// The game configured for this identity, or null where none is.
    /// </summary>
    /// <remarks>
    /// At most one, by construction: <see cref="LocalState.Games"/> is keyed by identity, so there is
    /// no list here to pick from and no way for two to disagree.
    /// </remarks>
    Game? Find(GameIdentity identity);

    /// <summary>
    /// The game a save on this profile re-applies to, or null where none follows it. See
    /// <see cref="ProfileApplyTarget"/> for why this is derived rather than chosen, and why it is one
    /// game rather than a list.
    /// </summary>
    Game? GetGameFollowing(GameIdentity scope, ActiveProfile profile);

    /// <summary>
    /// The game this adapter is configured for, connecting it first where it
    /// <see cref="GameRepository.ConnectsAutomatically">connects automatically</see> and is not connected yet. Null
    /// for an adapter that has settings to ask for and is not connected.
    /// </summary>
    /// <exception cref="UserFriendlyException">
    /// The game cannot be connected yet - for Farming Simulator, most likely because it has never
    /// been launched on this machine - or one of its folders already belongs to another game.
    /// </exception>
    Game? ConnectAutomatically(IBaseGameAdapter baseAdapter);

    /// <summary>
    /// Connects the game the adapter is configured for, as this machine has it.
    /// </summary>
    /// <remarks>
    /// <b>No name is asked for.</b> A game is called what its adapter calls it - Farming Simulator
    /// 25 - so connecting is filling in the settings form and nothing else. The name is still
    /// written down, because everything that says it does so without a hydrated adapter; see
    /// <see cref="PersistedGame.Name"/>.
    /// </remarks>
    /// <exception cref="UserFriendlyException">
    /// This game is already configured, or one of its folders is claimed.
    /// </exception>
    Game Create(IBaseGameAdapter baseAdapter, DynamicForm localSettings);

    /// <remarks>
    /// The name and the folder names are re-derived here as well as at
    /// <see cref="Create(IBaseGameAdapter, DynamicForm)"/>, so an adapter release that renames
    /// either catches up the next time anything is saved rather than needing the game reconnected.
    /// </remarks>
    void Update(Game game, IBaseGameAdapter baseAdapter, DynamicForm localSettings);

    /// <summary>
    /// Writes down the folders the adapter says this game reaches now, where they are not the ones
    /// already written down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For an adapter whose folders move without anybody touching its settings here - Farming
    /// Simulator reads its mod folder out of the game's own settings file. <see cref="Game.Targets"/> is
    /// what the drift check, the file watchers and folder claims read, none of them with an adapter to
    /// ask, so a folder that moved underneath them would be watched in the old place while an apply
    /// wrote to the new one.
    /// </para>
    /// <para>
    /// A no-op, with nothing saved or raised, when nothing moved - which is nearly always, so it is
    /// cheap enough to call before every apply and on every load of the repo list.
    /// </para>
    /// </remarks>
    /// <exception cref="UserFriendlyException">
    /// The adapter cannot be hydrated, or a folder it now reaches already belongs to another game.
    /// </exception>
    void RefreshTargets(Game game, IBaseGameAdapter baseAdapter);

    /// <param name="pinnedRevision">
    /// The revision to hold the game on, or null - nearly always - to follow head. See
    /// <see cref="PersistedGame.PinnedRevision"/>.
    /// </param>
    void SetActiveProfile(Game game, ActiveProfile? activeProfile, int? pinnedRevision = null);

    /// <summary>
    /// Stops every game tracking a profile that no longer exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called when a profile is permanently deleted from a repo's archive. An <em>archived</em>
    /// profile is deliberately still tracked - it exists, and everything pointing at it goes on
    /// pointing at it - so this is the one event that lets go, and it is the deletion rather than
    /// the archiving.
    /// </para>
    /// <para>
    /// Local state, which is why it lives here: the server has no idea which machines were pointed
    /// at the profile, and a game whose active profile is a dangling id would report drift
    /// against a mod list nobody can read.
    /// </para>
    /// </remarks>
    void StopTracking(Guid profileId);

    /// <summary>
    /// Disconnects the game: forgets its settings and which profile it follows. Nothing on disk is
    /// touched.
    /// </summary>
    /// <exception cref="UserFriendlyException">
    /// The game holds a savegame, whose check-out record would be lost with it, or something is
    /// working on its folders right now.
    /// </exception>
    void Delete(Game game);

    /// <summary>
    /// The first folder these settings could not claim, or null where every one of them is free.
    /// </summary>
    /// <remarks>
    /// Two rules, and they are what the cross-scope duplicate-folder check collapsed to: a game's
    /// targets are distinct from each other, and no folder belongs to two games. Only asked of
    /// settings that are valid in their own right - the adapter refuses to hydrate anything else.
    /// </remarks>
    /// <param name="ignoredGame">
    /// The game being edited, whose own folders are not a conflict with themselves.
    /// </param>
    FolderClaim? FindFolderConflict(IBaseGameAdapter baseAdapter, DynamicForm localSettings, GameIdentity? ignoredGame = null);
}
