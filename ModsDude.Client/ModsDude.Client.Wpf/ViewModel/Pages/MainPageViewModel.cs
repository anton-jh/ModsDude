using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class MainPageViewModel
    : PageViewModel, IDisposable
{
    private readonly RepoService _repoService;
    private readonly RepoPageViewModelFactory _repoPageViewModelFactory;
    private readonly IGameAdapterIndex _gameAdapterIndex;
    private readonly ObservableCollectionSynchronizer<RepoModel, IMenuItemViewModel, string> _reposSynchronizer;


    public MainPageViewModel(
        RepoService repoService,
        RepoPageViewModelFactory repoPageViewModelFactory,
        IGameAdapterIndex gameAdapterIndex,
        NavigationLockService navigationLockService)
    {
        MenuItems = [
            new MenuItemViewModel("Home", new ExamplePageViewModel("ModsDude Home")),
            new MenuItemViewModel("Create repo", () => new CreateRepoPageViewModel(repoService, gameAdapterIndex, navigationLockService))
        ];

        Repos = [];

        NavManager = new(navigationLockService)
        {
            Selected = MenuItems.First()
        };

        _repoService = repoService;
        _repoPageViewModelFactory = repoPageViewModelFactory;
        _gameAdapterIndex = gameAdapterIndex;
        _reposSynchronizer = new(_repoService.Repos, Repos, MapRepoToVm, x => x.Title);

        repoService.RepoOfInterestChanged += RepoOfInterestChanged;
    }
    

    public SidebarNavigationManager NavManager { get; }

    public ObservableCollection<IMenuItemViewModel> MenuItems { get; }

    public ObservableCollection<IMenuItemViewModel> Repos { get; }


    public override void Init()
    {
        LoadReposCommand.Execute(null);
    }

    public void Dispose()
    {
        _reposSynchronizer.Dispose();
        NavManager.Dispose();
    }


    [RelayCommand]
    private async Task LoadRepos(CancellationToken cancellationToken)
    {
        await _repoService.RefreshRepos(cancellationToken);
    }

    private async void RepoOfInterestChanged(Guid repoIdOfInterest)
    {
        var repo = Repos
            .OfType<RepoItemViewModel>()
            .FirstOrDefault(x => x.Id == repoIdOfInterest);

        NavManager.Selected = repo;
    }

    private RepoItemViewModel MapRepoToVm(RepoModel repo)
    {
        return new RepoItemViewModel(repo, _repoPageViewModelFactory);
    }
}
