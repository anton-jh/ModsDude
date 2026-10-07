using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Repos.Archive;
using ModsDude.Client.Wpf.Repos;
using ModsDude.Client.Wpf.Settings;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Shell;
public partial class MainPageViewModel
    : PageViewModel, IDisposable
{
    private readonly IRepoStore _repoStore;
    private readonly ILastSelectionRepository _lastSelectionRepository;
    private readonly RepoPageViewModel.Factory _repoPageViewModelFactory;
    private readonly IShellNavigationService _shellNavigationService;
    private readonly ObservableCollectionSynchronizer<Repo, MenuItemViewModel, string> _reposSynchronizer;

    /// <summary>
    /// Joining or creating a repo: the first of <see cref="PlaceItems"/>, and where the app lands when
    /// there is no repo to open.
    /// </summary>
    private readonly MenuItemViewModel _joinOrCreateMenuItem;

    private readonly ICurrentUserStore _currentUser;
    private readonly IProfileSyncStatusService _syncStatus;
    private readonly IConnectionRetry _connection;
    private readonly CancellationTokenSource _disposed = new();

    private bool _selectionRestored;


    public MainPageViewModel(
        IRepoStore repoStore,
        ILastSelectionRepository lastSelectionRepository,
        RepoPageViewModel.Factory repoPageViewModelFactory,
        IFactory<JoinOrCreatePageViewModel> joinOrCreatePageViewModelFactory,
        IFactory<SettingsPageViewModel> settingsPageViewModelFactory,
        IFactory<AccountPageViewModel> accountPageViewModelFactory,
        INavigationLockService navigationLockService,
        IShellNavigationService shellNavigationService,
        AccountViewModel account,
        ICurrentUserStore currentUser,
        IModalService modalService,
        IFactory<ArchivePageViewModel> archivePageViewModelFactory,
        IProfileSyncStatusService syncStatus,
        IConnectionRetry connection)
    {
        _currentUser = currentUser;
        _syncStatus = syncStatus;
        _connection = connection;

        _joinOrCreateMenuItem = new MenuItemViewModel("Join or create", joinOrCreatePageViewModelFactory.Create)
            .WithIcon(MenuIcons.JoinOrCreate);

        // Everything the rail leads to that is not a repo, straight after the last one.
        PlaceItems = [
            _joinOrCreateMenuItem,
            // A repo archived by any admin leaves every member's rail, so this is where somebody looks
            // when one they were using is suddenly not there.
            new MenuItemViewModel("Archive", archivePageViewModelFactory.Create).WithIcon(MenuIcons.Archive)
        ];

        // About who is using the app rather than places in it, so at the foot of the rail.
        AccountItems = [
            new MenuItemViewModel("Settings", settingsPageViewModelFactory.Create).WithIcon(MenuIcons.Settings),
            new AccountItemViewModel(account, accountPageViewModelFactory.Create)
        ];

        Repos = [];

        // Nothing until the repo list is in: the first load decides between the last repo and the
        // Join or create page, and showing either before then would be showing a guess.
        NavManager = new(navigationLockService, modalService);

        _repoStore = repoStore;
        _lastSelectionRepository = lastSelectionRepository;
        _repoPageViewModelFactory = repoPageViewModelFactory;
        _shellNavigationService = shellNavigationService;
        _reposSynchronizer = new(_repoStore.Repos, Repos, MapRepoToVm, x => x.Title, NaturalOrder.Comparer);

        // Two repos reading the same is a property of this list, and this list changes while the
        // user is looking at it - joining one, archiving one, or renaming one can make a pair
        // collide or stop colliding. So it is answered again on both, rather than once at build.
        Repos.CollectionChanged += OnReposChanged;
        ApplyTags();

        // One heading per game, because the rail's repos are only interchangeable within one. A game
        // installation belongs to a game, so two repos of one adapter configured for different games
        // offer disjoint game lists and nothing that works in one works in the other.
        ReposView = CollectionViewSource.GetDefaultView(Repos);
        ReposView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RepoItemViewModel.Game)));

        repoStore.RepoCreated += OnRepoCreated;
        repoStore.PendingChangesChanged += OnPendingRepoChangesChanged;
        NavManager.PropertyChanged += OnNavigationChanged;
        _syncStatus.Changed += OnSyncStatusChanged;

        _shellNavigationService.Register(this);
    }


    public NavigationManager NavManager { get; }

    public ObservableCollection<MenuItemViewModel> Repos { get; }

    /// <summary>
    /// The repo list as the rail draws it: the same entries, under one heading per game.
    /// </summary>
    /// <remarks>
    /// <b>Grouping only, no sorting of its own.</b> The order inside a group is the synchronizer's -
    /// naturally sorted by name, and kept that way through renames by moving the entry rather than
    /// rebuilding the list - and adding sort descriptions here would take that ordering over and then
    /// fail to notice a rename, which is the one thing the synchronizer exists to handle. Groups come
    /// out in the order their first repo does, which is stable for as long as the list is.
    /// </remarks>
    public ICollectionView ReposView { get; }

    /// <summary>Join or create, and the archive of repos.</summary>
    public IReadOnlyList<MenuItemViewModel> PlaceItems { get; }

    /// <summary>Settings and the account page.</summary>
    public IReadOnlyList<MenuItemViewModel> AccountItems { get; }

    /// <summary>
    /// Whether the server has repo changes the list does not show yet, which is the only time the
    /// rail offers Refresh - see <see cref="Shared.RemoteChangeWatcher"/>.
    /// </summary>
    public bool HasPendingRepoChanges => _repoStore.PendingChanges is not null;

    /// <summary>Refresh's label, which says what it would bring in.</summary>
    public string RefreshToolTip => _repoStore.PendingChanges is { } changes
        ? $"Refresh{Environment.NewLine}{changes.Describe()}"
        : "Refresh";


    protected override void Init()
    {
        LoadAtStart();
    }

    public void Dispose()
    {
        // Stops the first load retrying for a shell nobody is looking at any more.
        _disposed.Cancel();

        _shellNavigationService.Unregister(this);

        _repoStore.RepoCreated -= OnRepoCreated;
        _repoStore.PendingChangesChanged -= OnPendingRepoChangesChanged;
        NavManager.PropertyChanged -= OnNavigationChanged;
        _syncStatus.Changed -= OnSyncStatusChanged;
        Repos.CollectionChanged -= OnReposChanged;

        foreach (var entry in Repos.OfType<RepoItemViewModel>())
        {
            entry.PropertyChanged -= OnRepoEntryChanged;
        }

        _reposSynchronizer.Dispose();
        NavManager.Dispose();
    }

    /// <summary>
    /// Selects a repo and hands back the page it opened, for a deep link from outside the rail.
    /// </summary>
    /// <returns>
    /// Null where the repo is not one of this account's, or where the page in front of the user
    /// refused to be navigated away from.
    /// </returns>
    public async Task<RepoPageViewModel?> TrySelectRepoAsync(Guid repoId)
    {
        if (FindRepo(repoId) is null)
        {
            await LoadReposCommand.ExecuteAsync(null);
        }

        if (FindRepo(repoId) is not RepoItemViewModel entry)
        {
            return null;
        }

        if (ReferenceEquals(NavManager.Current, entry) is false)
        {
            NavManager.Selected = entry;
        }

        return NavManager.CurrentPage as RepoPageViewModel;
    }


    /// <summary>
    /// The first load of the signed-in user and their repos, retried until the server answers - see
    /// <see cref="ConnectionRetry"/>.
    /// </summary>
    /// <remarks>
    /// Async void for the same reason the command's own Execute rethrows: a failure that waiting will
    /// not fix still reaches the error modal on the UI thread.
    /// </remarks>
    private async void LoadAtStart()
    {
        // Each part skipped where it is already in: Refresh may have got the list while this was waiting.
        var attempt = async (CancellationToken cancellationToken) =>
        {
            await _currentUser.GetAsync(cancellationToken);

            if (_repoStore.HasLoaded is false)
            {
                await LoadReposCommand.ExecuteAsync(null);
            }
        };

        await _connection.RunAsync(ConnectionTarget.Server, attempt, _disposed.Token);
    }

    [RelayCommand]
    private async Task LoadRepos(CancellationToken cancellationToken)
    {
        await _repoStore.RefreshRepos(cancellationToken);

        SelectLandingPage();

        // Refresh got through while the first load was waiting out its interval, so that
        // wait is only keeping a notice up about a list that is already here.
        _connection.RetryNow();
    }

    /// <summary>
    /// Where the app opens: the repo last open, else the first one the rail lists, else Join or create.
    /// </summary>
    /// <remarks>
    /// Only on the first load. Refresh runs the same command, and jumping the user back to
    /// where they were an hour ago because they asked for fresh data would be its own bug. Nor where
    /// something was chosen while the list was still on its way.
    /// </remarks>
    private void SelectLandingPage()
    {
        if (_selectionRestored)
        {
            return;
        }

        _selectionRestored = true;

        if (NavManager.Selected is not null)
        {
            return;
        }

        var entries = Repos.OfType<RepoItemViewModel>().ToList();

        NavManager.Selected = _lastSelectionRepository.GetLastRepo(entries.Select(x => x.Id)) is Guid repoId
            ? entries.First(x => x.Id == repoId)
            : DefaultEntry();
    }

    /// <summary>
    /// Opens the default entry where the page has gone blank - which is the open repo leaving the list,
    /// archived or left, and the repo list letting go of it as its row went.
    /// </summary>
    /// <remarks>
    /// Looked at once the dispatcher is idle rather than at once, because every navigation passes through
    /// nothing on its way to the next page, and only a nothing still there afterwards is a blank page.
    /// </remarks>
    private void FallBackToDefault()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed.IsCancellationRequested is false && NavManager.Selected is null && NavManager.CurrentPage is null)
            {
                NavManager.Selected = DefaultEntry();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// The first repo as drawn, which is first in the first game's group - groups come in the order of
    /// their first repo, so that is the head of the list - or Join or create where there is none.
    /// </summary>
    private MenuItemViewModel DefaultEntry()
        => Repos.OfType<RepoItemViewModel>().FirstOrDefault() ?? _joinOrCreateMenuItem;

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationManager.CurrentPage) && NavManager.CurrentPage is null && _selectionRestored)
        {
            FallBackToDefault();
        }

        if (e.PropertyName == nameof(NavigationManager.Selected) &&
            NavManager.Selected is RepoItemViewModel repo)
        {
            _lastSelectionRepository.RecordRepo(repo.Id);
        }
    }

    /// <summary>
    /// Re-asks every repo entry whether the profile its game follows has drifted, because that can
    /// change while nothing about the list does.
    /// </summary>
    private void OnSyncStatusChanged(object? sender, EventArgs e)
    {
        foreach (var entry in Repos.OfType<RepoItemViewModel>())
        {
            entry.RefreshSyncState(_syncStatus);
        }
    }

    private void OnRepoCreated(Guid repoId)
    {
        if (Repos.OfType<RepoItemViewModel>().FirstOrDefault(x => x.Id == repoId) is RepoItemViewModel repo)
        {
            NavManager.Selected = repo;
        }
    }

    private void OnPendingRepoChangesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasPendingRepoChanges));
        OnPropertyChanged(nameof(RefreshToolTip));
    }

    private void OnReposChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The synchronizer inserts and removes one entry at a time, and a rename arrives as a Move,
        // so every action here is one where the set of names may have changed.
        foreach (var entry in e.OldItems?.OfType<RepoItemViewModel>() ?? [])
        {
            entry.PropertyChanged -= OnRepoEntryChanged;
        }

        foreach (var entry in e.NewItems?.OfType<RepoItemViewModel>() ?? [])
        {
            entry.PropertyChanged -= OnRepoEntryChanged;
            entry.PropertyChanged += OnRepoEntryChanged;
        }

        ApplyTags();
    }

    /// <summary>
    /// A renamed repo republishes its title, which is the only thing that can make two entries read
    /// the same without the list itself changing.
    /// </summary>
    private void OnRepoEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MenuItemViewModel.Title))
        {
            ApplyTags();
        }
    }

    /// <summary>
    /// Puts a tag on every repo that shares its name with another one here, and takes it off every
    /// repo that does not. Setting <c>Tag</c> does not republish <c>Title</c>, so this cannot feed
    /// itself through <see cref="OnRepoEntryChanged"/>.
    /// </summary>
    private void ApplyTags()
    {
        var entries = Repos.OfType<RepoItemViewModel>().ToList();
        var ambiguous = RepoDisplay.FindAmbiguous(entries.Select(x => (x.Id, x.Name)));

        foreach (var entry in entries)
        {
            entry.ShowTagIf(ambiguous.Contains(entry.Id));
        }
    }

    private RepoItemViewModel? FindRepo(Guid repoId)
        => Repos.OfType<RepoItemViewModel>().FirstOrDefault(x => x.Id == repoId);

    private RepoItemViewModel MapRepoToVm(Repo repo)
    {
        var entry = new RepoItemViewModel(repo, _repoPageViewModelFactory);
        entry.RefreshSyncState(_syncStatus);

        return entry;
    }
}
