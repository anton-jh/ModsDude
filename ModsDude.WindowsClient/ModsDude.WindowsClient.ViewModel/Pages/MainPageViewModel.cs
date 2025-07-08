using CommunityToolkit.Mvvm.Input;
using ModsDude.WindowsClient.Model.Services;
using ModsDude.WindowsClient.Utilities.GenericFactories;
using ModsDude.WindowsClient.ViewModel.ViewModelFactories;
using ModsDude.WindowsClient.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.WindowsClient.ViewModel.Pages;
public partial class MainPageViewModel
    : PageViewModel
{
    private readonly RepoService _repoService;
    private readonly IFactory<NewRepoItemViewModel> _newRepoItemViewModelFactory;
    private readonly RepoPageViewModelFactory _repoPageViewModelFactory;
    private NewRepoItemViewModel? _repoDraft;


    public MainPageViewModel(
        RepoService repoService,
        IFactory<NewRepoItemViewModel> newRepoItemViewModelFactory,
        RepoPageViewModelFactory repoPageViewModelFactory)
    {
        _selectedMenuItem = MenuItems.First();
        _repoService = repoService;
        _newRepoItemViewModelFactory = newRepoItemViewModelFactory;
        _repoPageViewModelFactory = repoPageViewModelFactory;
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

    public ObservableCollection<IMenuItemViewModel> MenuItems { get; } = [
        new MenuItemViewModel("Home", new ExamplePageViewModel("Home page")),
        new MenuItemViewModel("Test", new ExamplePageViewModel("Test page"))
    ];

    public ObservableCollection<IMenuItemViewModel> Repos { get; } = [];

    public string NewRepoButtonText => _repoDraft is not null
        ? "Cancel"
        : "New";


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

    [RelayCommand]
    private void ToggleCreateRepo()
    {
        if (_repoDraft is null)
        {
            _repoDraft = _newRepoItemViewModelFactory.Create();
            Repos.Insert(0, _repoDraft);
            SelectedMenuItem = _repoDraft;
            OnPropertyChanged(nameof(NewRepoButtonText));
        }
        else
        {
            StopCreateRepo();
        }
    }

    private void StopCreateRepo()
    {
        if (_repoDraft is not null)
        {
            Repos.Remove(_repoDraft);
            _repoDraft = null;
            OnPropertyChanged(nameof(NewRepoButtonText));
        }
    }

    private void RepoListChanged(Guid? repoIdOfInterest)
    {
        StopCreateRepo();
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
