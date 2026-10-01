using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Repos.Archive;
using ModsDude.Client.Wpf.Repos;
using ModsDude.Client.Wpf.Settings;
using ModsDude.Client.Wpf.Shared;
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
    private readonly IRepoRepository _repoService;
    private readonly ILastSelectionRepository _lastSelectionRepository;
    private readonly RepoPageViewModel.Factory _repoPageViewModelFactory;
    private readonly IShellNavigationService _shellNavigationService;
    private readonly ObservableCollectionSynchronizer<Repo, MenuItemViewModel, string> _reposSynchronizer;

    /// <summary>
    /// Create repo, one of <see cref="HeaderMenuItems"/>. Held on its own as well, because whether it is
    /// open to this account is decided here and the Welcome page offers it too.
    /// </summary>
    private readonly MenuItemViewModel _createRepoMenuItem;

    /// <summary>Join repo, one of <see cref="HeaderMenuItems"/>, and offered by the Welcome page.</summary>
    private readonly MenuItemViewModel _joinRepoMenuItem;

    /// <summary>
    /// The account page, reached from the account card at the foot of the sidebar: it is about who is
    /// using the app, not a place in it. Still an entry, so that selecting it opens the page the way
    /// every other way somewhere does.
    /// </summary>
    private readonly MenuItemViewModel _accountMenuItem;

    /// <summary>Settings, reached from the gear on the account card for the same reason as the account page.</summary>
    private readonly MenuItemViewModel _settingsMenuItem;

    /// <summary>
    /// What the page shows when there is no repo to show: no repos yet, or the open one just went away.
    /// Not in any list - nobody navigates to it, it is where the app lands.
    /// </summary>
    private readonly MenuItemViewModel _welcomeMenuItem;

    private readonly IProfileSyncStatusService _syncStatus;
    private readonly IConnectionRetry _connection;
    private readonly CancellationTokenSource _disposed = new();

    private bool _selectionRestored;


    public MainPageViewModel(
        IRepoRepository repoService,
        ILastSelectionRepository lastSelectionRepository,
        RepoPageViewModel.Factory repoPageViewModelFactory,
        JoinRepoPageViewModel.Factory joinRepoPageViewModelFactory,
        IFactory<SettingsPageViewModel> settingsPageViewModelFactory,
        IFactory<AccountPageViewModel> accountPageViewModelFactory,
        IGameAdapterIndex gameAdapterIndex,
        INavigationLockService navigationLockService,
        IShellNavigationService shellNavigationService,
        AccountViewModel account,
        IFilePickerService filePickerService,
        IModalService modalService,
        IFactory<ArchivePageViewModel> archivePageViewModelFactory,
        IProfileSyncStatusService syncStatus,
        IConnectionRetry connection)
    {
        Account = account;
        _syncStatus = syncStatus;
        _connection = connection;

        _createRepoMenuItem = new MenuItemViewModel("Create repo", () => new CreateRepoPageViewModel(repoService, gameAdapterIndex, navigationLockService, filePickerService, modalService))
            .WithIcon(MenuIcons.CreateRepo);

        _joinRepoMenuItem = new MenuItemViewModel("Join repo", joinRepoPageViewModelFactory.Create)
            .WithIcon(MenuIcons.JoinRepo);

        _accountMenuItem = new MenuItemViewModel("Account", accountPageViewModelFactory.Create);

        _settingsMenuItem = new MenuItemViewModel("Settings", settingsPageViewModelFactory.Create)
            .WithIcon(MenuIcons.Settings);

        _welcomeMenuItem = new MenuItemViewModel("Welcome", () => new WelcomePageViewModel(_joinRepoMenuItem, _createRepoMenuItem, Open));

        // Everything the repo list leads to that is not a repo. The sidebar holds nothing but the
        // list, so these are behind the "⋯" at its top rather than rows above it.
        HeaderMenuItems = [
            _joinRepoMenuItem,
            _createRepoMenuItem,
            // A repo archived by any admin leaves every member's sidebar, so this is where somebody
            // looks when one they were using is suddenly not there.
            new MenuItemViewModel("Archived repos", archivePageViewModelFactory.Create).WithIcon(MenuIcons.Archive)
        ];

        // Not a membership level: creating repos is gated on User.IsTrusted, a flag granted by hand
        // in the database. It arrives with the account's own record a moment after sign-in, so the
        // entry starts open and closes only once the answer is actually no.
        Account.PropertyChanged += OnAccountChanged;
        Account.OpenRequested += OnAccountOpenRequested;
        ApplyTrust();

        Repos = [];

        // Nothing until the repo list is in: the first load decides between the last repo and the
        // Welcome page, and showing either before then would be showing a guess.
        NavManager = new(navigationLockService, modalService);

        _repoService = repoService;
        _lastSelectionRepository = lastSelectionRepository;
        _repoPageViewModelFactory = repoPageViewModelFactory;
        _shellNavigationService = shellNavigationService;
        _reposSynchronizer = new(_repoService.Repos, Repos, MapRepoToVm, x => x.Title, NaturalOrder.Comparer);

        // Two repos reading the same is a property of this list, and this list changes while the
        // user is looking at it - joining one, archiving one, or renaming one can make a pair
        // collide or stop colliding. So it is answered again on both, rather than once at build.
        Repos.CollectionChanged += OnReposChanged;
        ApplyTags();

        // One heading per game, because the sidebar's repos are only interchangeable within one. An
        // game belongs to a game, so two repos of one adapter configured for different games
        // offer disjoint game lists and nothing that works in one works in the other; running
        // them together in one alphabetical column made that invisible.
        ReposView = CollectionViewSource.GetDefaultView(Repos);
        ReposView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RepoItemViewModel.Game)));

        repoService.RepoCreated += OnRepoCreated;
        repoService.PendingChangesChanged += OnPendingRepoChangesChanged;
        NavManager.PropertyChanged += OnNavigationChanged;
        _syncStatus.Changed += OnSyncStatusChanged;

        _shellNavigationService.Register(this);
    }


    /// <summary>
    /// Outlives this page rather than belonging to it: switching user is what replaces the shell.
    /// </summary>
    public AccountViewModel Account { get; }

    public NavigationManager NavManager { get; }

    /// <summary>The entries behind the "⋯" at the top of the repo list, under Refresh.</summary>
    public IReadOnlyList<MenuItemViewModel> HeaderMenuItems { get; }

    public ObservableCollection<MenuItemViewModel> Repos { get; }

    /// <summary>
    /// The repo list as the sidebar draws it: the same entries, under one heading per game.
    /// </summary>
    /// <remarks>
    /// <b>Grouping only, no sorting of its own.</b> The order inside a group is the synchronizer's -
    /// naturally sorted by name, and kept that way through renames by moving the entry rather than
    /// rebuilding the list - and adding sort descriptions here would take that ordering over and then
    /// fail to notice a rename, which is the one thing the synchronizer exists to handle. Groups come
    /// out in the order their first repo does, which is stable for as long as the list is.
    /// </remarks>
    public ICollectionView ReposView { get; }

    /// <summary>
    /// Whether this sidebar is a rail: while a repo is open, its own sidebar is the deepest one there is,
    /// and this one is what is left of the way there. Opening it again is a hover away.
    /// </summary>
    public bool IsSidebarCollapsed => NavManager.CurrentPage is RepoPageViewModel;

    /// <summary>Whether one of the header menu's pages is showing, for the "⋯" to draw as selected.</summary>
    public bool IsHeaderMenuSelected => NavManager.Selected is { } selected && HeaderMenuItems.Contains(selected);

    /// <summary>Whether the settings page is showing, for the card's gear to draw as selected.</summary>
    public bool IsSettingsSelected => ReferenceEquals(NavManager.Selected, _settingsMenuItem);

    /// <summary>
    /// Whether the server has repo changes the list does not show yet. Brought in by the menu's
    /// Refresh and nothing else - see <see cref="Shared.RemoteChangeWatcher"/>.
    /// </summary>
    public bool HasPendingRepoChanges => _repoService.PendingChanges is not null;

    /// <summary>What the menu's Refresh would bring in, drawn under it. Null while the server has said nothing.</summary>
    public string? PendingRepoChangesText => _repoService.PendingChanges?.Describe();

    /// <summary>The "⋯"'s tooltip, which says what is waiting when something is.</summary>
    public string MenuToolTip => _repoService.PendingChanges is { } changes
        ? $"{changes.Describe()}{Environment.NewLine}{Environment.NewLine}Refresh from this menu to bring the changes in."
        : "Refresh, join, create and archived repos";


    protected override void Init()
    {
        LoadInitialRepos();
    }

    public void Dispose()
    {
        // Stops the first load retrying for a shell nobody is looking at any more.
        _disposed.Cancel();

        _shellNavigationService.Unregister(this);

        Account.PropertyChanged -= OnAccountChanged;
        Account.OpenRequested -= OnAccountOpenRequested;
        _repoService.RepoCreated -= OnRepoCreated;
        _repoService.PendingChangesChanged -= OnPendingRepoChangesChanged;
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
    /// Selects a repo and hands back the page it opened, for a deep link from outside the sidebar.
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

        if (ReferenceEquals(NavManager.Selected, entry) is false)
        {
            NavManager.Selected = entry;
        }

        return NavManager.CurrentPage as RepoPageViewModel;
    }


    /// <summary>
    /// The first load, retried until the server answers - see <see cref="ConnectionRetry"/>.
    /// </summary>
    /// <remarks>
    /// Async void for the same reason the command's own Execute rethrows: a failure that waiting will
    /// not fix still reaches the error modal on the UI thread, as it did before this retried at all.
    /// </remarks>
    private async void LoadInitialRepos()
    {
        // Skipped where Refresh already got the list in while this was waiting.
        var attempt = (CancellationToken _) => _repoService.HasLoaded ? Task.CompletedTask : LoadReposCommand.ExecuteAsync(null);

        if (await _connection.RunAsync(ConnectionTarget.Server, attempt, _disposed.Token))
        {
            // Asked for alongside the list and missing for the same reason, and nothing else asks again.
            await Account.RefreshIdentityIfMissingAsync();
        }
    }

    /// <summary>Opens a page that is not a row in the repo list - one from the header's menu, say.</summary>
    [RelayCommand]
    private void Open(MenuItemViewModel entry)
    {
        NavManager.Selected = entry;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        NavManager.Selected = _settingsMenuItem;
    }

    private void OnAccountOpenRequested(object? sender, EventArgs e)
    {
        NavManager.Selected = _accountMenuItem;
    }

    [RelayCommand]
    private async Task LoadRepos(CancellationToken cancellationToken)
    {
        await _repoService.RefreshRepos(cancellationToken);

        SelectLandingPage();

        // Refresh got through while the first load was waiting out its interval, so that
        // wait is only keeping a notice up about a list that is already here.
        _connection.RetryNow();
    }

    /// <summary>
    /// Where the app opens: the repo last open, else the first one the sidebar lists, else Welcome.
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

        // First as drawn, which is first in the first game's group - and groups come in the order of
        // their first repo, so that is the head of the list.
        NavManager.Selected = _lastSelectionRepository.GetLastRepo(entries.Select(x => x.Id)) is Guid repoId
            ? entries.First(x => x.Id == repoId)
            : entries.FirstOrDefault() ?? _welcomeMenuItem;
    }

    /// <summary>
    /// Puts Welcome up where the page has gone blank - which is the open repo leaving the list, archived or
    /// left, and the repo list letting go of it as its row went.
    /// </summary>
    /// <remarks>
    /// Looked at once the dispatcher is idle rather than at once, because every navigation passes through
    /// nothing on its way to the next page, and only a nothing still there afterwards is a blank page.
    /// </remarks>
    private void FallBackToWelcome()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed.IsCancellationRequested is false && NavManager.Selected is null && NavManager.CurrentPage is null)
            {
                NavManager.Selected = _welcomeMenuItem;
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationManager.CurrentPage))
        {
            OnPropertyChanged(nameof(IsSidebarCollapsed));
        }

        if (e.PropertyName == nameof(NavigationManager.Selected))
        {
            OnPropertyChanged(nameof(IsHeaderMenuSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
        }

        if (e.PropertyName == nameof(NavigationManager.CurrentPage) && NavManager.CurrentPage is null && _selectionRestored)
        {
            FallBackToWelcome();
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

    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.IsTrusted))
        {
            ApplyTrust();
        }
    }

    /// <summary>
    /// Null means the answer has not arrived; only an explicit false closes the entry, so a slow
    /// round trip never briefly tells a trusted user they cannot create repos.
    /// </summary>
    private void ApplyTrust()
    {
        _createRepoMenuItem.RestrictIf(
            Account.IsTrusted is false,
            "Creating repos is granted by hand. Ask whoever runs this server to enable it for your account.");
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
        OnPropertyChanged(nameof(PendingRepoChangesText));
        OnPropertyChanged(nameof(MenuToolTip));
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
