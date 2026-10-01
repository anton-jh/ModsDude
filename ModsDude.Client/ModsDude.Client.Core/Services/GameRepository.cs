using ModsDude.Client.Core.Concurrency;
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


public class GameRepository : IGameRepository
{
    private readonly IStateStore _store;
    private readonly ISyncManifestStore _manifestStore;
    private readonly IResourceLeases _leases;


    public GameRepository(IStateStore store, ISyncManifestStore manifestStore, IResourceLeases leases)
    {
        _store = store;
        _manifestStore = manifestStore;
        _leases = leases;

        Games = new(store.Read(state => state.Games.Select(x => new Game(x.Key, x.Value)).ToList()));

        // At startup as well as after every edit, because the state can arrive smaller than the
        // manifest directory without anything here having run: a version bump discards it wholesale,
        // and the manifests it named survive on disk as files nothing will ever look for again.
        DropStaleManifests();
    }


    public ObservableCollection<Game> Games { get; }

    public event EventHandler? GameChanged;


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

    public Game? GetGameFollowing(GameIdentity scope, ActiveProfile profile)
    {
        return ProfileApplyTarget.Find(Games, scope, profile);
    }

    /// <summary>
    /// Whether this adapter's game is connected without asking anybody anything - true where its
    /// local settings form has no fields, so there is nothing a connect page could ask.
    /// </summary>
    /// <remarks>
    /// Such a game cannot be configured or disconnected either: connecting only writes the game down
    /// and touches nothing on disk, and a game that follows no profile does nothing at all, so there
    /// is no state a user would choose over "connected".
    /// </remarks>
    public static bool ConnectsAutomatically(IBaseGameAdapter baseAdapter)
    {
        return baseAdapter.GetLocalSettingsTemplate().HasFields() is false;
    }

    public Game? ConnectAutomatically(IBaseGameAdapter baseAdapter)
    {
        if (Find(baseAdapter.Scope) is Game existing)
        {
            return existing;
        }

        if (ConnectsAutomatically(baseAdapter) is false)
        {
            return null;
        }

        return Create(baseAdapter, baseAdapter.GetLocalSettingsTemplate());
    }

    public Game Create(IBaseGameAdapter baseAdapter, DynamicForm localSettings)
    {
        var identity = baseAdapter.Scope;

        // Refused rather than merged or renumbered: the state is keyed by identity, so a second
        // record for the same game has nowhere to be written, and silently replacing the first would
        // take away its active profile and its savegame holds.
        if (Find(identity) is not null)
        {
            throw new UserFriendlyException(
                "This game is already connected",
                "A machine configures a game once - one active profile, one savegame hold, however many folders its adapter reaches - and this one already is. Open its settings to change where those folders are.");
        }

        var targets = GetTargets(baseAdapter, localSettings);

        EnsureFoldersAreUnclaimed(targets, null);

        var persistedModel = new PersistedGame()
        {
            GameAdapterId = baseAdapter.Id,
            Name = baseAdapter.GameDisplayName,
            AdapterLocalSettings = localSettings.Serialize(),
            Targets = [.. targets]
        };

        var game = new Game(identity, persistedModel);

        _store.Update(state => state.Games[identity] = persistedModel);
        Games.Add(game);

        return game;
    }

    public void Update(Game game, IBaseGameAdapter baseAdapter, DynamicForm localSettings)
    {
        var targets = GetTargets(baseAdapter, localSettings);

        EnsureFoldersAreUnclaimed(targets, game.Identity);

        _store.Update(_ => game.Update(baseAdapter.GameDisplayName, localSettings, targets));
        game.NotifySettingsChanged();

        // A field somebody emptied has taken a target away, and the manifest describing what used to
        // be in that folder is one nothing will look for again. Dropped here rather than worked out,
        // because a removal and an adapter author renaming a key are the same edit from this side.
        DropStaleManifests();

        // The mod folders may have moved, which makes every answer about the old ones meaningless.
        GameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshTargets(Game game, IBaseGameAdapter baseAdapter)
    {
        var localSettings = game.GetLocalSettings(baseAdapter);
        var targets = GetTargets(baseAdapter, localSettings);

        if (targets.SequenceEqual(game.Targets))
        {
            return;
        }

        Update(game, baseAdapter, localSettings);
    }

    public void SetActiveProfile(Game game, ActiveProfile? activeProfile, int? pinnedRevision = null)
    {
        _store.Update(_ => game.SetActiveProfile(activeProfile, pinnedRevision));
        game.NotifyActiveProfileChanged();

        // A folder that was in sync with one profile is drifted from another the moment it is pointed
        // at it, and nothing about the collection changed to say so.
        GameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopTracking(Guid profileId)
    {
        var affected = Games
            .Where(x => x.ActiveProfile?.ProfileId == profileId)
            .ToList();

        if (affected.Count == 0)
        {
            return;
        }

        // One save for the batch: they were all made unusable by one event.
        _store.Update(_ =>
        {
            foreach (var game in affected)
            {
                game.SetActiveProfile(null);
            }
        });

        foreach (var game in affected)
        {
            game.NotifyActiveProfileChanged();
        }

        GameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(Game game)
    {
        using var lease = _leases.TryAcquireExclusive(
            game.TargetRefs.Select(ResourceKeys.Target),
            $"Disconnecting '{game.Name}'")
            ?? throw new UserFriendlyException(
                $"'{game.Name}' is busy",
                $"Something is working on the folders of '{game.Name}', so it was not disconnected.");

        var outcome = DisconnectOutcome.AlreadyGone;

        _store.UpdateIf(state =>
        {
            outcome = Disconnect(state, game.Identity);

            return outcome is DisconnectOutcome.Removed;
        });

        if (outcome is DisconnectOutcome.HoldsSavegame)
        {
            throw new UserFriendlyException(
                "Check in the savegame first",
                $"'{game.Name}' holds a checked-out savegame, so it was not disconnected.");
        }

        Games.Remove(game);

        // Nothing reads a manifest for a folder no game reaches any more, and leaving one behind
        // would keep a few hundred kilobytes per disconnected folder forever.
        DropStaleManifests();
    }

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

    internal enum DisconnectOutcome
    {
        Removed,
        AlreadyGone,
        HoldsSavegame
    }

    /// <summary>
    /// Removes the game from the state unless it holds a savegame.
    /// </summary>
    /// <remarks>
    /// Over the state rather than through the store, so the rule can be exercised without a real
    /// <c>state.json</c> - see <see cref="FindFolderConflict(IEnumerable{Game}, IReadOnlyList{PersistedModTarget}, GameIdentity?)"/>.
    /// </remarks>
    internal static DisconnectOutcome Disconnect(LocalState state, GameIdentity identity)
    {
        if (state.Games.TryGetValue(identity, out var persisted) is false)
        {
            return DisconnectOutcome.AlreadyGone;
        }

        if (persisted.SavegameCheckouts.Count > 0)
        {
            return DisconnectOutcome.HoldsSavegame;
        }

        state.Games.Remove(identity);

        return DisconnectOutcome.Removed;
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
