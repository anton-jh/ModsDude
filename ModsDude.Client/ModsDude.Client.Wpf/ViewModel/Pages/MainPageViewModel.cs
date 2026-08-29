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
    private readonly ObservableCollectionSynchronizer<Repo, MenuItemViewModel, string> _reposSynchronizer;

    private bool _selectionRestored;


    public MainPageViewModel(
        RepoRepository repoService,
        LastSelectionRepository lastSelectionRepository,
        RepoPageViewModel.Factory repoPageViewModelFactory,
        IFactory<SettingsPageViewModel> settingsPageViewModelFactory,
        IGameAdapterIndex gameAdapterIndex,
        NavigationLockService navigationLockService,
        IDialogService dialogService,
        IModalService modalService)
    {
        MenuItems = [
            new MenuItemViewModel("Home", () => new ExamplePageViewModel("ModsDude", "Home")),
            new MenuItemViewModel("Create repo", () => new CreateRepoPageViewModel(repoService, gameAdapterIndex, navigationLockService, dialogService, modalService)),
            new MenuItemViewModel("Settings", settingsPageViewModelFactory.Create)
        ];

        Repos = [];

        NavManager = new(navigationLockService, modalService)
        {
            Selected = MenuItems.First()
        };

        _repoService = repoService;
        _lastSelectionRepository = lastSelectionRepository;
        _repoPageViewModelFactory = repoPageViewModelFactory;
        _reposSynchronizer = new(_repoService.Repos, Repos, MapRepoToVm, x => x.Title);

        repoService.RepoCreated += OnRepoCreated;
        NavManager.PropertyChanged += OnNavigationChanged;
    }


    public NavigationManager NavManager { get; }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public ObservableCollection<MenuItemViewModel> Repos { get; }


    protected override void Init()
    {
        LoadReposCommand.Execute(null);
    }

    public void Dispose()
    {
        _repoService.RepoCreated -= OnRepoCreated;
        NavManager.PropertyChanged -= OnNavigationChanged;

        _reposSynchronizer.Dispose();
        NavManager.Dispose();
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

    private void OnRepoCreated(Guid repoId)
    {
        if (Repos.OfType<RepoItemViewModel>().FirstOrDefault(x => x.Id == repoId) is RepoItemViewModel repo)
        {
            NavManager.Selected = repo;
        }
    }

    private RepoItemViewModel MapRepoToVm(Repo repo)
    {
        return new RepoItemViewModel(repo, _repoPageViewModelFactory);
    }
}
