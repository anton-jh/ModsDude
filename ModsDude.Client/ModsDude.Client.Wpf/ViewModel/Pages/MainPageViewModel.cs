using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using ModsDude.Shared.GenericFactories;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class MainPageViewModel
    : PageViewModel, IDisposable
{
    private readonly RepoRepository _repoService;
    private readonly LastSelectionRepository _lastSelectionRepository;
    private readonly RepoPageViewModel.Factory _repoPageViewModelFactory;
    private readonly ShellNavigationService _shellNavigationService;
    private readonly ObservableCollectionSynchronizer<Repo, MenuItemViewModel, string> _reposSynchronizer;

    private readonly MenuItemViewModel _createRepoMenuItem;

    private bool _selectionRestored;


    public MainPageViewModel(
        RepoRepository repoService,
        LastSelectionRepository lastSelectionRepository,
        RepoPageViewModel.Factory repoPageViewModelFactory,
        JoinRepoPageViewModel.Factory joinRepoPageViewModelFactory,
        IFactory<SettingsPageViewModel> settingsPageViewModelFactory,
        IGameAdapterIndex gameAdapterIndex,
        NavigationLockService navigationLockService,
        ShellNavigationService shellNavigationService,
        AccountViewModel account,
        IDialogService dialogService,
        IModalService modalService)
    {
        Account = account;

        _createRepoMenuItem = new MenuItemViewModel("Create repo", () => new CreateRepoPageViewModel(repoService, gameAdapterIndex, navigationLockService, dialogService, modalService));

        MenuItems = [
            new MenuItemViewModel("Home", () => new ExamplePageViewModel("ModsDude", "Home")),
            _createRepoMenuItem,
            new MenuItemViewModel("Join repo", joinRepoPageViewModelFactory.Create),
            new MenuItemViewModel("Settings", settingsPageViewModelFactory.Create)
        ];

        // Not a membership level: creating repos is gated on User.IsTrusted, a flag granted by hand
        // in the database. It arrives with the account's own record a moment after sign-in, so the
        // entry starts open and closes only once the answer is actually no.
        Account.PropertyChanged += OnAccountChanged;
        ApplyTrust();

        Repos = [];

        NavManager = new(navigationLockService, modalService)
        {
            Selected = MenuItems.First()
        };

        _repoService = repoService;
        _lastSelectionRepository = lastSelectionRepository;
        _repoPageViewModelFactory = repoPageViewModelFactory;
        _shellNavigationService = shellNavigationService;
        _reposSynchronizer = new(_repoService.Repos, Repos, MapRepoToVm, x => x.Title, NaturalOrder.Comparer);

        repoService.RepoCreated += OnRepoCreated;
        NavManager.PropertyChanged += OnNavigationChanged;

        _shellNavigationService.Register(this);
    }


    /// <summary>
    /// Outlives this page rather than belonging to it: switching user is what replaces the shell.
    /// </summary>
    public AccountViewModel Account { get; }

    public NavigationManager NavManager { get; }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public ObservableCollection<MenuItemViewModel> Repos { get; }


    protected override void Init()
    {
        LoadReposCommand.Execute(null);
    }

    public void Dispose()
    {
        _shellNavigationService.Unregister(this);

        Account.PropertyChanged -= OnAccountChanged;
        _repoService.RepoCreated -= OnRepoCreated;
        NavManager.PropertyChanged -= OnNavigationChanged;

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


    [RelayCommand]
    private async Task LoadRepos(CancellationToken cancellationToken)
    {
        await _repoService.RefreshRepos(cancellationToken);

        RestoreLastSelectedRepo();
    }

    /// <summary>
    /// Only on the first load. The refresh button runs the same command, and jumping the user back to
    /// where they were an hour ago because they asked for fresh data would be its own bug.
    /// </summary>
    private void RestoreLastSelectedRepo()
    {
        if (_selectionRestored)
        {
            return;
        }

        _selectionRestored = true;

        var entries = Repos.OfType<RepoItemViewModel>().ToList();

        if (_lastSelectionRepository.GetLastRepo(entries.Select(x => x.Id)) is not Guid repoId)
        {
            return;
        }

        NavManager.Selected = entries.First(x => x.Id == repoId);
    }

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationManager.Selected) &&
            NavManager.Selected is RepoItemViewModel repo)
        {
            _lastSelectionRepository.RecordRepo(repo.Id);
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

    private RepoItemViewModel? FindRepo(Guid repoId)
        => Repos.OfType<RepoItemViewModel>().FirstOrDefault(x => x.Id == repoId);

    private RepoItemViewModel MapRepoToVm(Repo repo)
    {
        return new RepoItemViewModel(repo, _repoPageViewModelFactory);
    }
}
