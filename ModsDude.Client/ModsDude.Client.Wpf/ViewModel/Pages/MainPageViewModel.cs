using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
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
        IGameAdapterIndex gameAdapterIndex)
    {
        MenuItems = [
            new MenuItemViewModel("Home", new ExamplePageViewModel("ModsDude Home")),
            new MenuItemViewModel("Create repo", () => new CreateRepoPageViewModel(repoService, gameAdapterIndex))
        ];

        _selectedMenuItem = MenuItems.First();
        CurrentPage = _selectedMenuItem.GetPage();
        _repoService = repoService;
        _repoPageViewModelFactory = repoPageViewModelFactory;
        _gameAdapterIndex = gameAdapterIndex;

        Repos = [];
        _reposSynchronizer = new(_repoService.Repos, Repos, MapRepoToVm, x => x.Title);

        repoService.RepoOfInterestChanged += RepoOfInterestChanged;
    }
    

    private IMenuItemViewModel? _selectedMenuItem;
    public IMenuItemViewModel? SelectedMenuItem
    {
        get => _selectedMenuItem;
        set
        {
            OnPropertyChanging(nameof(SelectedMenuItem));
            _selectedMenuItem = null;
            OnPropertyChanged(nameof(SelectedMenuItem));

            OnPropertyChanging(nameof(SelectedMenuItem));
            _selectedMenuItem = value;
            OnPropertyChanged(nameof(SelectedMenuItem));

            OnPropertyChanging(nameof(CurrentPage));
            CurrentPage = SelectedMenuItem?.GetPage();
            OnPropertyChanged(nameof(CurrentPage));

            CurrentPage?.Init();
        }
    }

    public PageViewModel? CurrentPage { get; private set; }

    public ObservableCollection<IMenuItemViewModel> MenuItems { get; }

    public ObservableCollection<IMenuItemViewModel> Repos { get; }


    public override void Init()
    {
        LoadReposCommand.Execute(null);
    }

    public void Dispose()
    {
        _reposSynchronizer.Dispose();
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

        SelectedMenuItem = repo;
    }

    private RepoItemViewModel MapRepoToVm(RepoModel repo)
    {
        return new RepoItemViewModel(repo, _repoPageViewModelFactory);
    }
}
