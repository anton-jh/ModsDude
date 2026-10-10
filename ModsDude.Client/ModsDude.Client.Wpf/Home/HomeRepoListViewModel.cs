using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Friends;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Repos;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Home;

/// <summary>
/// Every repo, under the game it is for: how it stands on this machine, who is playing in it, and a
/// way into each of its pages.
/// </summary>
/// <remarks>
/// <b>Drawn from stores other pages share</b>, and drawn again whenever one of them says it changed,
/// so Home and the repo pages cannot disagree. Read for this list alone: the names of held savegames,
/// the profiles of the repos the games follow, and a repo's profiles once its picker is opened.
/// </remarks>
public sealed class HomeRepoListViewModel : ObservableObject, IDisposable
{
    private readonly FriendActivityListViewModel _friends;
    private readonly IGameRepository _games;
    private readonly IRepoStore _repos;
    private readonly IProfileStore _profiles;
    private readonly IProfileSyncStatusService _syncStatus;
    private readonly IProfileApplyService _applyService;
    private readonly IDriftMonitor _driftMonitor;
    private readonly IGameRunningMonitor _runningGames;
    private readonly ISavegameBindingStore _bindings;
    private readonly IHeldSavegameNames _heldNames;
    private readonly ISavegameCheckInFlow _checkIn;
    private readonly IShellNavigationService _navigation;
    private readonly IToastService _toasts;
    private readonly ILogger<HomeRepoListViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly LatestLoad _namesLoad;
    private readonly HashSet<Repo> _watchedRepos = [];

    /// <summary>The names of the savegames held here, by savegame id, as of the last read.</summary>
    private IReadOnlyDictionary<Guid, HeldSavegameName> _savegameNames = new Dictionary<Guid, HeldSavegameName>();


    public HomeRepoListViewModel(
        FriendActivityListViewModel friends,
        IGameRepository games,
        IRepoStore repos,
        IProfileStore profiles,
        IProfileSyncStatusService syncStatus,
        IProfileApplyService applyService,
        IDriftMonitor driftMonitor,
        IGameRunningMonitor runningGames,
        ISavegameBindingStore bindings,
        IHeldSavegameNames heldNames,
        ISavegameCheckInFlow checkIn,
        IShellNavigationService navigation,
        IToastService toasts,
        ILogger<HomeRepoListViewModel> logger)
    {
        _friends = friends;
        _games = games;
        _repos = repos;
        _profiles = profiles;
        _syncStatus = syncStatus;
        _applyService = applyService;
        _driftMonitor = driftMonitor;
        _runningGames = runningGames;
        _bindings = bindings;
        _heldNames = heldNames;
        _checkIn = checkIn;
        _navigation = navigation;
        _toasts = toasts;
        _logger = logger;
        _namesLoad = new(_ => { }, _lifetime.Token);

        _friends.Rows.CollectionChanged += OnCollectionChanged;
        _games.Games.CollectionChanged += OnCollectionChanged;
        _games.GameChanged += OnChanged;
        _repos.Repos.CollectionChanged += OnReposChanged;
        _profiles.Changed += OnProfilesChanged;
        _syncStatus.Changed += OnChanged;
        _runningGames.Changed += OnChanged;
        _bindings.BindingsChanged += OnBindingsChanged;

        WatchRepos();
        Rebuild();
    }


    public ObservableCollection<HomeGameGroupViewModel> Groups { get; } = [];


    public void Dispose()
    {
        _friends.Rows.CollectionChanged -= OnCollectionChanged;
        _games.Games.CollectionChanged -= OnCollectionChanged;
        _games.GameChanged -= OnChanged;
        _repos.Repos.CollectionChanged -= OnReposChanged;
        _profiles.Changed -= OnProfilesChanged;
        _syncStatus.Changed -= OnChanged;
        _runningGames.Changed -= OnChanged;
        _bindings.BindingsChanged -= OnBindingsChanged;

        foreach (var repo in _watchedRepos)
        {
            repo.PropertyChanged -= OnRepoPropertyChanged;
        }

        _watchedRepos.Clear();

        // Cancelled but not disposed: a redraw already queued on the dispatcher may still read its token.
        _lifetime.Cancel();
    }

    /// <summary>Reads the names of the held savegames and the profiles the games follow. Off the UI thread.</summary>
    public async Task InitAsync()
    {
        await await Application.Current.Dispatcher.InvokeAsync(ReadSavegameNamesAsync);

        var followed = _games.Games
            .Select(x => x.ActiveProfile?.RepoId)
            .OfType<Guid>()
            .Distinct()
            .Order()
            .ToList();

        foreach (var repoId in followed)
        {
            try
            {
                await _profiles.EnsureLoadedAsync(repoId, _lifetime.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogInformation(exception, "Could not read the profiles of repo {RepoId} to name the profile a game follows.", repoId);
            }
        }

        Post(Rebuild);
    }


    internal async Task LoadProfilesAsync(HomeRepoRowViewModel row, CancellationToken cancellationToken)
    {
        row.ProfilesFailed = false;

        try
        {
            await _profiles.EnsureLoadedAsync(row.RepoId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not read the profiles of repo {RepoId} for the profile picker on Home.", row.RepoId);

            row.ProfilesFailed = true;
        }
    }

    /// <summary>Puts the repo's game on the profile picked, through the activation every page uses.</summary>
    internal async Task ActivateAsync(HomeRepoRowViewModel row, HomeProfileOption option)
    {
        if (option.IsFollowed || FindRepo(row.RepoId) is not Repo repo || repo.Games.FirstOrDefault() is not Game game)
        {
            return;
        }

        // Moving a game onto a different profile takes the previous one's mods back out, so the plan
        // is shown first.
        var outcome = await _applyService.ActivateAsync(
            repo, game, option.ProfileId, option.Name, confirmPlan: true, progress: null, CancellationToken.None);

        _toasts.Show(outcome.Message, outcome.ToastSeverity);

        await _driftMonitor.CheckAsync();
    }

    internal async Task RunActionAsync(HomeRepoRowViewModel row)
    {
        var state = row.State;

        switch (state.Action)
        {
            case HomeRepoAction.ConnectGame:
                await _navigation.GoToAsync(state.RepoId, new RepoDestination.ConnectGame());
                break;
            case HomeRepoAction.Review when state.FollowedProfileId is Guid profileId:
                await _navigation.GoToAsync(state.RepoId, new RepoDestination.ProfileMods(profileId, state.ReviewTarget));
                break;
            case HomeRepoAction.Apply when state.FollowedProfileId is Guid profileId:
                await ApplyAsync(state.RepoId, profileId, state.FollowedProfileName);
                break;
            case HomeRepoAction.OpenProfile when state.FollowedProfileId is Guid profileId:
                await _navigation.GoToAsync(state.RepoId, new RepoDestination.Profile(profileId));
                break;
        }
    }

    internal Task OpenSectionAsync(HomeRepoRowViewModel row, RepoSection section)
        => _navigation.GoToAsync(row.RepoId, new RepoDestination.Section(section));

    internal Task CheckInAsync(HomeHeldSavegame savegame)
        => savegame.Name is string name
            ? _checkIn.CheckInHeldAsync(savegame.Game, savegame.SavegameId, name, CancellationToken.None)
            : Task.CompletedTask;


    /// <summary>A pure apply: the game already follows this profile, which is why it is out of step with it.</summary>
    private async Task ApplyAsync(Guid repoId, Guid profileId, string? profileName)
    {
        if (FindRepo(repoId) is not Repo repo || repo.Games.FirstOrDefault() is not Game game)
        {
            return;
        }

        var outcome = await _applyService.ApplyAsync(
            repo, game, profileId, profileName, confirmPlan: false, progress: null, CancellationToken.None);

        _toasts.Show(outcome.Message, outcome.ToastSeverity);

        await _driftMonitor.CheckAsync();
    }

    private Repo? FindRepo(Guid repoId) => _repos.Repos.FirstOrDefault(x => x.Id == repoId);

    private void OnChanged(object? sender, EventArgs e) => Post(Rebuild);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Post(Rebuild);

    private void OnProfilesChanged(Guid repoId) => Post(Rebuild);

    private void OnRepoPropertyChanged(object? sender, PropertyChangedEventArgs e) => Post(Rebuild);

    private void OnReposChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WatchRepos();
        Post(Rebuild);
    }

    /// <remarks>Async void as an event handler: the load it awaits observes its own failures.</remarks>
    private async void OnBindingsChanged(object? sender, EventArgs e)
    {
        Post(Rebuild);

        await await Application.Current.Dispatcher.InvokeAsync(ReadSavegameNamesAsync);
    }

    private void Post(Action action) => Application.Current?.Dispatcher.InvokeAsync(() =>
    {
        if (_lifetime.IsCancellationRequested is false)
        {
            action();
        }
    });

    /// <summary>
    /// Follows every listed repo's own changes - a rename, a new membership level, a game setting
    /// that moves it to another game - none of which changes the list itself.
    /// </summary>
    private void WatchRepos()
    {
        var listed = _repos.Repos.ToHashSet();

        foreach (var gone in _watchedRepos.Where(x => listed.Contains(x) is false).ToList())
        {
            gone.PropertyChanged -= OnRepoPropertyChanged;
            _watchedRepos.Remove(gone);
        }

        foreach (var added in listed.Where(_watchedRepos.Add))
        {
            added.PropertyChanged += OnRepoPropertyChanged;
        }
    }

    /// <summary>Reads what the held savegames are called. On the UI thread.</summary>
    private Task ReadSavegameNamesAsync()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        return _namesLoad.RunAsync(
            async cancellationToken =>
            {
                var names = new Dictionary<Guid, HeldSavegameName>();

                foreach (var game in _games.Games.OrderBy(x => x.Identity.ToString(), StringComparer.Ordinal).ToList())
                {
                    foreach (var (savegameId, name) in await _heldNames.ReadAsync(game, null, cancellationToken))
                    {
                        names[savegameId] = name;
                    }
                }

                return names;
            },
            names =>
            {
                _savegameNames = names;
                Rebuild();
            },
            exception =>
            {
                _logger.LogInformation(exception, "Could not read the names of the savegames held here.");

                return Task.CompletedTask;
            });
    }

    private void Rebuild()
    {
        var repos = _repos.Repos.ToList();
        var ambiguous = RepoDisplay.FindAmbiguous(repos.Select(x => (x.Id, x.Name)));
        var playing = _friends.Rows.Where(x => x.IsPlaying).ToLookup(x => x.Model.RepoId);

        var groups = repos
            .GroupBy(x => x.Scope)
            .Select(x => new
            {
                Scope = x.Key,
                State = CreateGameState(x.First()),
                Repos = x
                    .OrderBy(repo => repo.Name, NaturalOrder.Comparer)
                    .ThenBy(repo => repo.Id)
                    .Select(repo => CreateRepoState(repo, ambiguous.Contains(repo.Id), playing[repo.Id]))
                    .ToList()
            })
            .OrderBy(x => x.State.Name, NaturalOrder.Comparer)
            .ThenBy(x => x.Scope.ToString(), StringComparer.Ordinal)
            .ToList();

        KeyedReconcile.Apply(
            Groups,
            groups,
            x => x.Scope,
            x => x.Scope,
            x =>
            {
                var group = new HomeGameGroupViewModel(x.Scope, x.State);
                ReconcileRows(group, x.Repos);

                return group;
            },
            (group, x) =>
            {
                group.State = x.State;
                ReconcileRows(group, x.Repos);
            });
    }

    private void ReconcileRows(HomeGameGroupViewModel group, IReadOnlyList<HomeRepoState> states)
        => KeyedReconcile.Apply(
            group.Repos,
            states,
            x => x.RepoId,
            x => x.RepoId,
            x => new HomeRepoRowViewModel(this, x),
            (row, state) => row.State = state);

    private HomeGameState CreateGameState(Repo repo)
    {
        var game = repo.Games.FirstOrDefault();

        return new HomeGameState(
            repo.Adapter.GameDisplayName,
            game is not null,
            game is not null && _runningGames.IsRunning(game.Identity));
    }

    private HomeRepoState CreateRepoState(Repo repo, bool isAmbiguous, IEnumerable<FriendActivityRowViewModel> playing)
    {
        var game = repo.Games.FirstOrDefault();
        var followedId = _syncStatus.ActiveProfileOf(repo);
        var syncState = _syncStatus.StateOf(repo);
        var canPickProfile = game is not null && repo.Adapter.CanSupportMods;
        var friends = playing.ToList();

        var action = game is null
            ? GameRepository.ConnectsAutomatically(repo.Adapter) ? HomeRepoAction.None : HomeRepoAction.ConnectGame
            : syncState switch
            {
                ProfileSyncState.Drifted => HomeRepoAction.Review,
                ProfileSyncState.NotApplied or ProfileSyncState.Applying => HomeRepoAction.Apply,
                ProfileSyncState.InSync => HomeRepoAction.OpenProfile,
                _ => HomeRepoAction.None
            };

        var profiles = canPickProfile
            ? _profiles.Live(repo.Id)
                .OrderBy(x => x.Name, NaturalOrder.Comparer)
                .ThenBy(x => x.Id)
                .Select(x => new HomeProfileOption(x.Id, x.Name, x.Id == followedId))
                .ToList()
            : [];

        var held = game is not null
            ? _bindings.GetBindings(game.Identity)
                .Where(x => x.RepoId == repo.Id)
                .OrderBy(x => x.SavegameId)
                .Select(x => new HomeHeldSavegame(game, x.SavegameId, _savegameNames.GetValueOrDefault(x.SavegameId)?.Name))
                .ToList()
            : [];

        return new HomeRepoState(
            repo.Id,
            repo.Name,
            isAmbiguous ? repo.Tag : null,
            repo.MembershipLevel,
            followedId,
            _profiles.Find(repo.Id, followedId)?.Name,
            game?.PinnedRevision,
            syncState,
            syncState is ProfileSyncState.Drifted && game is not null ? FindDriftedTarget(game) : null,
            action,
            game is not null && _applyService.IsBusy(repo, game),
            canPickProfile,
            _profiles.IsLoaded(repo.Id),
            profiles,
            [.. friends.Select(x => x.Avatar)],
            string.Join(", ", friends.Select(x => x.Name)),
            [.. RepoSections.Of(repo).Where(x => x.Section is not RepoSection.Overview)],
            held);
    }

    /// <summary>The first folder of the game that has moved, for Review to open already scanned.</summary>
    private ModTargetRef? FindDriftedTarget(Game game)
        => _driftMonitor.Drifted
            .Where(x => x.Game.Identity == game.Identity && (x.Report.Status is DriftStatus.Drifted || x.Report.HasSavegameDrift))
            .Select(x => x.Target)
            .OfType<GameModFolder>()
            .OrderBy(x => x.ModFolder, StringComparer.OrdinalIgnoreCase)
            .Select(x => (ModTargetRef?)x.Target)
            .FirstOrDefault();


    public sealed class Factory(IServiceProvider serviceProvider)
    {
        public HomeRepoListViewModel Create(FriendActivityListViewModel friends)
            => ActivatorUtilities.CreateInstance<HomeRepoListViewModel>(serviceProvider, friends);
    }
}
