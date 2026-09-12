using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Sync;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// One folder these settings cannot claim, and what has it already.
/// </summary>
/// <param name="Owner">
/// The game already using the folder, or null where the settings collide with <em>themselves</em> -
/// two of this game's own targets pointing at one folder. Both are refusals; only the sentence
/// differs.
/// </param>
public sealed record FolderClaim(string Path, Game? Owner)
{
    public string Describe()
    {
        return Owner is Game owner
            ? $"'{Path}' already belongs to '{owner.Name}'."
            : $"Two of this game's folders are both '{Path}'.";
    }
}


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

        Games = new(_state.Games.Select(x => new Game(x.Key, x.Value)));

        // At startup as well as after every edit, because the state can arrive smaller than the
        // manifest directory without anything here having run: a version bump discards it wholesale,
        // and the manifests it named survive on disk as files nothing will ever look for again.
        DropStaleManifests();
    }


    /// <summary>Every game configured on this machine.</summary>
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


    /// <summary>
    /// The game configured for this identity, or null where none is.
    /// </summary>
    /// <remarks>
    /// At most one, by construction: <see cref="LocalState.Games"/> is keyed by identity, so there is
    /// no list here to pick from and no way for two to disagree.
    /// </remarks>
    public Game? Find(GameIdentity identity)
    {
        return Games.FirstOrDefault(x => x.Identity == identity);
    }

    /// <summary>
    /// Every mod folder on this machine, one entry per folder: sync's store eviction is relying on
    /// this to know what a sweep must leave alone, and a game on a disk this store serves is running
    /// entries the sweep would otherwise drop.
    /// </summary>
    /// <remarks>
    /// One entry per <em>folder</em> rather than per game, so a game reaching three of them spares
    /// all three. Read off the persisted list, so it works for a game whose identity no loaded repo
    /// serves - and each entry carries the key its own manifest is filed under, which is what lets a
    /// sweep ask what that folder is running.
    /// </remarks>
    public IReadOnlyList<GameModFolder> GetAll()
    {
        return [.. Games.SelectMany(TargetsOf)];
    }

    /// <summary>
    /// Every game the drift check has to look at. A game whose identity no repo on this machine
    /// serves still owns its folders and still has a standing intent, and the check runs off both
    /// without hydrating an adapter.
    /// </summary>
    /// <remarks>
    /// One candidate per game carrying every folder it reaches, rather than one per folder: the mod
    /// half of the check is per folder and the savegame half is per game, and a candidate per folder
    /// would hash a game's held saves once for each of them.
    /// </remarks>
    public IReadOnlyList<DriftCandidate> GetDriftCandidates()
    {
        return [.. Games.Select(game => new DriftCandidate(
            game.Identity,
            game.Name,
            [.. TargetsOf(game)],
            game.ActiveProfile))];
    }

    /// <summary>Every folder one game reaches, each addressed by the key its manifest is under.</summary>
    private static IEnumerable<GameModFolder> TargetsOf(Game game)
    {
        return game.Targets.Select(x => new GameModFolder(new ModTargetRef(game.Identity, x.Key), x.ModFolder));
    }

    /// <summary>
    /// The games a save on this profile re-applies to. See <see cref="ProfileApplyTargets"/> for
    /// why this is derived rather than chosen.
    /// </summary>
    public IReadOnlyList<Game> GetGamesUsing(ActiveProfile profile)
    {
        return ProfileApplyTargets.Derive(Games, profile);
    }

    /// <exception cref="UserFriendlyException">
    /// This game is already configured, or one of its folders is claimed.
    /// </exception>
    public Game Create(IBaseGameAdapter baseAdapter, string name, DynamicForm localSettings)
    {
        var identity = baseAdapter.Scope;

        // Refused rather than merged or renumbered: the state is keyed by identity, so a second
        // record for the same game has nowhere to be written, and silently replacing the first would
        // take away its active profile and its savegame holds.
        if (_state.Games.ContainsKey(identity))
        {
            throw new UserFriendlyException(
                "This game is already connected",
                "One installation of a game is configured once on a machine, and this one already is. Open its settings to change where its folders are.");
        }

        var targets = GetTargets(baseAdapter, localSettings);

        EnsureFoldersAreUnclaimed(targets, null);

        var persistedModel = new PersistedGame()
        {
            GameAdapterId = baseAdapter.Id,
            Name = name,
            AdapterLocalSettings = localSettings.Serialize(),
            Targets = [.. targets]
        };

        var game = new Game(identity, persistedModel);

        _state.Games[identity] = persistedModel;
        Games.Add(game);
        _store.Save();

        return game;
    }

    public void Update(Game game, IBaseGameAdapter baseAdapter, string name, DynamicForm localSettings)
    {
        var targets = GetTargets(baseAdapter, localSettings);

        EnsureFoldersAreUnclaimed(targets, game.Identity);

        game.Update(name, localSettings, targets);
        _store.Save();

        // A field somebody emptied has taken a target away, and the manifest describing what used to
        // be in that folder is one nothing will look for again. Dropped here rather than worked out,
        // because a removal and an adapter author renaming a key are the same edit from this side.
        DropStaleManifests();

        // The mod folders may have moved, which makes every answer about the old ones meaningless.
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
        _state.Games.Remove(game.Identity);
        Games.Remove(game);
        _store.Save();

        // Nothing reads a manifest for a folder no game reaches any more, and leaving one behind
        // would keep a few hundred kilobytes per disconnected folder forever.
        DropStaleManifests();
    }

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
    public FolderClaim? FindFolderConflict(IBaseGameAdapter baseAdapter, DynamicForm localSettings, GameIdentity? ignoredGame = null)
    {
        return FindFolderConflict(Games, GetTargets(baseAdapter, localSettings), ignoredGame);
    }

    /// <summary>
    /// Every target the adapter says a game with these settings would reach, as it gets written down.
    /// </summary>
    /// <remarks>
    /// Every target, not the one: this is the list that gets persisted, and it is the only thing that
    /// has to be complete for eviction to spare a folder nothing else can name. The key travels with
    /// the path because a path on its own names no manifest. A game reaching no folder claims none,
    /// which is the same empty list this returns for an adapter with no mod capability at all - both
    /// mean "nothing here can collide with anybody".
    /// </remarks>
    public static IReadOnlyList<PersistedModTarget> GetTargets(IBaseGameAdapter baseAdapter, DynamicForm localSettings)
    {
        return [.. baseAdapter
            .WithLocalSettings(localSettings)
            .GetLocalCapabilityAdapterFactory<ILocalModAdapter>()
            ?.Invoke()
            .ModTargets
            .Select(x => new PersistedModTarget(x.Key, x.Path)) ?? []];
    }


    /// <inheritdoc cref="FindFolderConflict(IBaseGameAdapter, DynamicForm, GameIdentity?)"/>
    /// <remarks>
    /// Over a list of games rather than over this repository's own, so the rule can be exercised
    /// without a real <c>state.json</c> - <see cref="Persistence.Store{T}"/> writes to a fixed path
    /// under LocalAppData, and a test of it as-is would rewrite the developer's own game list.
    /// </remarks>
    internal static FolderClaim? FindFolderConflict(
        IEnumerable<Game> games,
        IReadOnlyList<PersistedModTarget> targets,
        GameIdentity? ignoredGame)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var folder = targets[i].ModFolder;

            // Its own, first: two targets of one game pointing at one folder would have each of them
            // uninstalling what the other just put there.
            for (var earlier = 0; earlier < i; earlier++)
            {
                if (FileSystemHelper.ArePathsEqual(targets[earlier].ModFolder, folder))
                {
                    return new FolderClaim(folder, null);
                }
            }

            var owner = games.FirstOrDefault(game =>
                game.Identity != ignoredGame &&
                game.Targets.Any(x => FileSystemHelper.ArePathsEqual(x.ModFolder, folder)));

            if (owner is not null)
            {
                return new FolderClaim(folder, owner);
            }
        }

        return null;
    }

    /// <summary>
    /// Leaves a manifest for every folder some game still reaches, and nothing else.
    /// </summary>
    /// <remarks>
    /// Called after anything that changes which folders exist - and from the constructor, because a
    /// discarded state file changes that without any of those having run. Read off the persisted
    /// targets, so a game whose identity no loaded repo serves keeps its manifests rather than
    /// having them swept for being unreadable right now.
    /// </remarks>
    private void DropStaleManifests()
    {
        _manifestStore.DropStale(Games.SelectMany(x => x.TargetRefs));
    }

    private void EnsureFoldersAreUnclaimed(IReadOnlyList<PersistedModTarget> targets, GameIdentity? ignoredGame)
    {
        if (FindFolderConflict(Games, targets, ignoredGame) is FolderClaim claim)
        {
            throw new UserFriendlyException(
                "That folder is already in use",
                $"A folder may only be claimed by one game's target: {claim.Describe()}");
        }
    }
}
