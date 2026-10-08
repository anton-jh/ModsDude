using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Friends;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace ModsDude.Client.Wpf.Home;

/// <summary>
/// Where the app opens: what friends are playing, with the button that joins them, beside the games
/// on this machine and the savegames they hold.
/// </summary>
/// <remarks>
/// <b>Everything here is drawn from stores other pages share</b>, and drawn again whenever one of them
/// says it changed. Nothing is fetched for this page alone except the names it needs to say what is
/// held, so Home and the repo pages cannot disagree.
/// </remarks>
public sealed partial class HomePageViewModel : PageViewModel, IDisposable
{
    private readonly IGameRepository _games;
    private readonly IRepoStore _repos;
    private readonly IProfileStore _profiles;
    private readonly IProfileSyncStatusService _syncStatus;
    private readonly IGameRunningMonitor _runningGames;
    private readonly ISavegameBindingStore _bindings;
    private readonly IHeldSavegameNames _heldNames;
    private readonly ISavegameCheckInFlow _checkIn;
    private readonly IShellNavigationService _navigation;
    private readonly ILogger<HomePageViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly LatestLoad _namesLoad;

    /// <summary>The names of the savegames held here, by savegame id, as of the last read.</summary>
    private IReadOnlyDictionary<Guid, HeldSavegameName> _savegameNames = new Dictionary<Guid, HeldSavegameName>();


    public HomePageViewModel(
        FriendActivityListViewModel.Factory friendsFactory,
        IGameRepository games,
        IRepoStore repos,
        IProfileStore profiles,
        IProfileSyncStatusService syncStatus,
        IGameRunningMonitor runningGames,
        ISavegameBindingStore bindings,
        IHeldSavegameNames heldNames,
        ISavegameCheckInFlow checkIn,
        IShellNavigationService navigation,
        ILogger<HomePageViewModel> logger)
    {
        _games = games;
        _repos = repos;
        _profiles = profiles;
        _syncStatus = syncStatus;
        _runningGames = runningGames;
        _bindings = bindings;
        _heldNames = heldNames;
        _checkIn = checkIn;
        _navigation = navigation;
        _logger = logger;
        _namesLoad = new(_ => { }, _lifetime.Token);

        Friends = friendsFactory.CreateForAllRepos();

        _games.Games.CollectionChanged += OnCollectionChanged;
        _games.GameChanged += OnChanged;
        _repos.Repos.CollectionChanged += OnCollectionChanged;
        _profiles.Changed += OnProfilesChanged;
        _syncStatus.Changed += OnChanged;
        _runningGames.Changed += OnChanged;
        _bindings.BindingsChanged += OnBindingsChanged;

        Rebuild();
    }


    /// <summary>Every friend's game in every repo, whoever is playing first.</summary>
    public FriendActivityListViewModel Friends { get; }

    public ObservableCollection<HomeGameViewModel> Games { get; } = [];

    public ObservableCollection<HomeSavegameViewModel> Savegames { get; } = [];

    public bool HasRepos => _repos.Repos.Count > 0;

    public bool HasNoRepos => HasRepos is false;

    public bool HasGames => Games.Count > 0;

    public bool HasNoGames => HasGames is false;

    public bool HasSavegames => Savegames.Count > 0;

    public bool HasNoSavegames => HasSavegames is false;


    public void Dispose()
    {
        _games.Games.CollectionChanged -= OnCollectionChanged;
        _games.GameChanged -= OnChanged;
        _repos.Repos.CollectionChanged -= OnCollectionChanged;
        _profiles.Changed -= OnProfilesChanged;
        _syncStatus.Changed -= OnChanged;
        _runningGames.Changed -= OnChanged;
        _bindings.BindingsChanged -= OnBindingsChanged;

        // Cancelled but not disposed: a redraw already queued on the dispatcher may still read its token.
        _lifetime.Cancel();

        Friends.Dispose();
    }


    protected override async Task InitAsync()
    {
        await Friends.RefreshAsync();

        await await Application.Current.Dispatcher.InvokeAsync(ReadSavegameNamesAsync);

        // The profiles every game here follows, so each tile can name its own.
        var followed = _games.Games
            .Select(x => x.ActiveProfile?.RepoId)
            .OfType<Guid>()
            .Distinct()
            .Order();

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
    }

    protected override void OnInitCompleted() => Rebuild();


    [RelayCommand]
    private Task OpenGame(HomeGameViewModel game)
        => game.Game.ActiveProfile is ActiveProfile active
            ? _navigation.GoToProfileAsync(active.RepoId, active.ProfileId)
            : Task.CompletedTask;

    [RelayCommand]
    private Task CheckIn(HomeSavegameViewModel savegame)
        => savegame.Name is string name
            ? _checkIn.CheckInHeldAsync(savegame.Game, savegame.SavegameId, name, CancellationToken.None)
            : Task.CompletedTask;

    [RelayCommand]
    private void JoinOrCreate() => _navigation.GoToJoinOrCreate();


    private void OnChanged(object? sender, EventArgs e) => Post(Rebuild);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Post(Rebuild);

    private void OnProfilesChanged(Guid repoId) => Post(Rebuild);

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

    /// <summary>Reads what the held savegames are called, every repo's named with its repo. On the UI thread.</summary>
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
        var games = _games.Games
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Identity.ToString(), StringComparer.Ordinal)
            .ToList();

        Games.Clear();

        foreach (var game in games)
        {
            Games.Add(CreateTile(game));
        }

        Savegames.Clear();

        foreach (var game in games)
        {
            foreach (var binding in _bindings.GetBindings(game.Identity).OrderBy(x => x.SavegameId))
            {
                var name = _savegameNames.GetValueOrDefault(binding.SavegameId);

                Savegames.Add(new HomeSavegameViewModel(game, binding.SavegameId, name?.Name, name?.OtherRepoName));
            }
        }

        OnPropertyChanged(nameof(HasRepos));
        OnPropertyChanged(nameof(HasNoRepos));
        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(HasNoGames));
        OnPropertyChanged(nameof(HasSavegames));
        OnPropertyChanged(nameof(HasNoSavegames));
    }

    private HomeGameViewModel CreateTile(Game game)
    {
        var active = game.ActiveProfile;
        var repo = active is ActiveProfile followed ? _repos.Repos.FirstOrDefault(x => x.Id == followed.RepoId) : null;
        var profile = active is ActiveProfile followedProfile
            ? _profiles.Live(followedProfile.RepoId).FirstOrDefault(x => x.Id == followedProfile.ProfileId)
            : null;

        return new HomeGameViewModel(
            game,
            profile?.Name,
            repo?.Name,
            _runningGames.IsRunning(game.Identity),
            repo is not null ? _syncStatus.StateOf(repo) : ProfileSyncState.None,
            _bindings.GetBindings(game.Identity).Count);
    }
}
