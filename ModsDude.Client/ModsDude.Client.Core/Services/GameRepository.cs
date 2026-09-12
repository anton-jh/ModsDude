using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Sync;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

public class GameRepository : IModFolders, IDriftCandidateSource
{
    private readonly StateStore _store;
    private readonly SyncManifestStore _manifestStore;
    private readonly LocalState _state;


    public GameRepository(StateStore store, SyncManifestStore manifestStore)
    {
        _store = store;
        _manifestStore = manifestStore;
        _state = store.Get();

        Games = new(_state.Games.Values.Select(x => new Game(x)));
    }


    /// <summary>Every game on this machine, across all scopes.</summary>
    public ObservableCollection<Game> Games { get; }

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
    public event EventHandler? GameChanged;


    public IEnumerable<Game> GetByScope(GameIdentity scope)
    {
        return Games.Where(x => x.Scope == scope);
    }

    /// <summary>
    /// The folders sync's store eviction has to know about, across every scope: a game on a
    /// disk this store serves is relying on entries the sweep would otherwise drop.
    /// </summary>
    public IReadOnlyList<InstanceModFolder> GetAll()
    {
        return [.. Games
            .Where(x => x.ModFolder is not null)
            .Select(x => new InstanceModFolder(x.Id, x.ModFolder!))];
    }

    /// <summary>
    /// Every game the drift check has to look at, across every scope. A game whose scope no
    /// repo on this machine serves still owns its folder and still has a standing intent, and the
    /// check runs off both without hydrating an adapter.
    /// </summary>
    public IReadOnlyList<DriftCandidate> GetDriftCandidates()
    {
        return [.. Games.Select(x => new DriftCandidate(x.Id, x.Name, x.ModFolder, x.ActiveProfile))];
    }

    /// <summary>
    /// The games a save on this profile re-applies to. See <see cref="ProfileApplyTargets"/> for
    /// why this is derived rather than chosen.
    /// </summary>
    public IReadOnlyList<Game> GetGamesUsing(ActiveProfile profile)
    {
        return ProfileApplyTargets.Derive(Games, profile);
    }

    public Game Create(IBaseGameAdapter baseAdapter, string name, DynamicForm localSettings)
    {
        var modFolder = GetModFolder(baseAdapter, localSettings);

        EnsureFolderIsUnclaimed(modFolder, null);

        var persistedModel = new PersistedGame()
        {
            Id = Guid.NewGuid(),
            Scope = baseAdapter.Scope,
            GameAdapterId = baseAdapter.Id,
            Name = name,
            AdapterLocalSettings = localSettings.Serialize(),
            ModFolder = modFolder
        };

        var game = new Game(persistedModel);

        _state.Games[persistedModel.Id] = persistedModel;
        Games.Add(game);
        _store.Save();

        return game;
    }

    public void Update(Game game, IBaseGameAdapter baseAdapter, string name, DynamicForm localSettings)
    {
        var modFolder = GetModFolder(baseAdapter, localSettings);

        EnsureFolderIsUnclaimed(modFolder, game.Id);

        game.Update(name, localSettings, modFolder);
        _store.Save();

        // The mod folder may have moved, which makes every answer about the old one meaningless.
        GameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetActiveProfile(Game game, ActiveProfile? activeProfile)
    {
        game.SetActiveProfile(activeProfile);
        _store.Save();

        // A folder that was in sync with one profile is drifted from another the moment it is pointed
        // at it, and nothing about the collection changed to say so.
        GameChanged?.Invoke(this, EventArgs.Empty);
    }

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
    public void StopTracking(Guid profileId)
    {
        var affected = Games
            .Where(x => x.ActiveProfile?.ProfileId == profileId)
            .ToList();

        if (affected.Count == 0)
        {
            return;
        }

        foreach (var game in affected)
        {
            game.SetActiveProfile(null);
        }

        // One save for the batch: they were all made unusable by one event.
        _store.Save();

        GameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(Game game)
    {
        _state.Games.Remove(game.Id);
        Games.Remove(game);
        _store.Save();

        // Nothing reads a manifest for a game that no longer exists, and leaving one behind
        // would keep a few hundred kilobytes per disconnected game folder forever.
        _manifestStore.Delete(game.Id);
    }

    /// <summary>
    /// The game already claiming the folder these settings point at, if any. Checked across
    /// every scope: two scopes can name the same folder, and only one game can own it.
    /// </summary>
    public Game? FindFolderConflict(IBaseGameAdapter baseAdapter, DynamicForm localSettings, Guid? ignoredInstanceId = null)
    {
        return FindFolderConflict(GetModFolder(baseAdapter, localSettings), ignoredInstanceId);
    }

    /// <summary>The mod folder the adapter says a game with these settings would own.</summary>
    /// <remarks>
    /// Takes the one target, which is Phase 10 slice 1 scaffolding. A game is still one folder,
    /// and the check this feeds - no two of them own the same one - is rewritten in slice 2a, where a
    /// game owns a list of folders instead.
    /// <para>
    /// A game reaching no folder claims none, which is the same null this already returns for an
    /// adapter with no mod capability at all - both mean "nothing here can collide with anybody".
    /// </para>
    /// </remarks>
    public static string? GetModFolder(IBaseGameAdapter baseAdapter, DynamicForm localSettings)
    {
        return baseAdapter
            .WithLocalSettings(localSettings)
            .GetLocalCapabilityAdapterFactory<ILocalModAdapter>()
            ?.Invoke()
            .ModTargets
            .SingleTargetOrNone()
            ?.Path;
    }


    private Game? FindFolderConflict(string? modFolder, Guid? ignoredInstanceId)
    {
        if (modFolder is null)
        {
            return null;
        }

        return Games.FirstOrDefault(x =>
            x.Id != ignoredInstanceId &&
            FileSystemHelper.ArePathsEqual(x.ModFolder, modFolder));
    }

    private void EnsureFolderIsUnclaimed(string? modFolder, Guid? ignoredInstanceId)
    {
        if (FindFolderConflict(modFolder, ignoredInstanceId) is Game owner)
        {
            throw new UserFriendlyException(
                $"'{owner.Name}' already uses that folder",
                $"A folder may only be claimed by one game: '{modFolder}' belongs to '{owner.Name}'.");
        }
    }
}
