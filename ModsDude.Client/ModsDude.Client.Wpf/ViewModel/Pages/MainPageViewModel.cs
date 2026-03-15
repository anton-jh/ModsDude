using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using ModsDude.Shared.GenericFactories;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class MainPageViewModel
    : PageViewModel
{
    private readonly RepoService _repoService;
    private readonly RepoPageViewModelFactory _repoPageViewModelFactory;
    private readonly IGameAdapterIndex _gameAdapterIndex;


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
        _repoService = repoService;
        _repoPageViewModelFactory = repoPageViewModelFactory;
        _gameAdapterIndex = gameAdapterIndex;
        repoService.RepoListChanged += RepoListChanged;
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
            OnPropertyChanging(nameof(CurrentPage));
            _selectedMenuItem = value;
            OnPropertyChanged(nameof(SelectedMenuItem));
            OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public PageViewModel? CurrentPage => SelectedMenuItem?.GetPage();

    public ObservableCollection<IMenuItemViewModel> MenuItems { get; }

    public ObservableCollection<IMenuItemViewModel> Repos { get; } = [];


    public override void Init()
    {
        LoadReposCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LoadRepos(CancellationToken cancellationToken)
    {
        var repos = await _repoService.GetRepos(cancellationToken);
        var viewModels = repos.Select(x => new RepoItemViewModel(x, _repoPageViewModelFactory));

        Repos.Clear();
        foreach (var repo in viewModels)
        {
            Repos.Add(repo);
        }
    }

    private void RepoListChanged(Guid? repoIdOfInterest)
    {
        LoadReposCommand.Execute(null);

        if (repoIdOfInterest is not null)
        {
            var repo = Repos
                .OfType<RepoItemViewModel>()
                .FirstOrDefault(x => x.Id == repoIdOfInterest);

            SelectedMenuItem = repo;
        }
    }
}
