using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class MainPageViewModel
    : PageViewModel, IDisposable
{
    private readonly RepoService _repoService;
    private readonly RepoPageViewModel.Factory _repoPageViewModelFactory;
    private readonly ObservableCollectionSynchronizer<RepoModel, MenuItemViewModel, string> _reposSynchronizer;


    public MainPageViewModel(
        RepoService repoService,
        RepoPageViewModel.Factory repoPageViewModelFactory,
        IGameAdapterIndex gameAdapterIndex,
        NavigationLockService navigationLockService,
        IDialogService dialogService,
        IModalService modalService)
    {
        MenuItems = [
            new MenuItemViewModel("Home", () => new ExamplePageViewModel("ModsDude", "Home")),
            new MenuItemViewModel("Create repo", () => new CreateRepoPageViewModel(repoService, gameAdapterIndex, navigationLockService, dialogService, modalService))
        ];

        Repos = [];

        NavManager = new(navigationLockService, modalService)
        {
            Selected = MenuItems.First()
        };

        _repoService = repoService;
        _repoPageViewModelFactory = repoPageViewModelFactory;
        _reposSynchronizer = new(_repoService.Repos, Repos, MapRepoToVm, x => x.Title);

        repoService.RepoOfInterestChanged += RepoOfInterestChanged;
    }
    

    public NavigationManager NavManager { get; }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public ObservableCollection<MenuItemViewModel> Repos { get; }


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
