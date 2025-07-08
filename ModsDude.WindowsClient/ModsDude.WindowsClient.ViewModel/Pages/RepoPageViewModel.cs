using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.WindowsClient.Model.Models;
using ModsDude.WindowsClient.Model.Services;
using ModsDude.WindowsClient.ViewModel.ViewModelFactories;
using ModsDude.WindowsClient.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace ModsDude.WindowsClient.ViewModel.Pages;
public partial class RepoPageViewModel
    : PageViewModel
{
    private readonly RepoModel _repo;
    private readonly RepoAdminPageViewModelFactory _repoAdminPageViewModelFactory;
    private readonly ProfileService _profileService;


    public RepoPageViewModel(
        RepoModel repo,
        RepoAdminPageViewModelFactory repoAdminPageViewModelFactory,
        ProfileService profileService)
    {
        _repo = repo;
        _repoAdminPageViewModelFactory = repoAdminPageViewModelFactory;
        _profileService = profileService;
        _name = repo.Name;

        CreateMenu();
        _selectedMenuItem = MenuItems.First();
    }


    [ObservableProperty]
    private string _name;

    private IMenuItemViewModel? _selectedMenuItem;
    public IMenuItemViewModel? SelectedMenuItem
    {
        get => _selectedMenuItem;
        set
        {
            OnPropertyChanging(nameof(SelectedMenuItem));
            _selectedMenuItem = null!;
            OnPropertyChanged(nameof(SelectedMenuItem));

            OnPropertyChanging(nameof(SelectedMenuItem));
            OnPropertyChanging(nameof(CurrentPage));
            _selectedMenuItem = value;
            OnPropertyChanged(nameof(SelectedMenuItem));
            OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public PageViewModel? CurrentPage => SelectedMenuItem?.GetPage();

    public ObservableCollection<IMenuItemViewModel> MenuItems { get; private set; }

    public ObservableCollection<IMenuItemViewModel> Instances { get; } = [
        new MenuItemViewModel("Game", new ExamplePageViewModel("Instance (Game)")),
        new MenuItemViewModel("Dedicated server", new ExamplePageViewModel("Instance (Dedicated server)"))
    ];

    public ObservableCollection<IMenuItemViewModel> Profiles { get; } = [];


    public override void Init()
    {
        LoadProfilesCommand.Execute(null);
    }


    [MemberNotNull(nameof(MenuItems))]
    private void CreateMenu()
    {
        MenuItems = [
            new MenuItemViewModel("Overview", new ExamplePageViewModel($"Repo overview ({Name})")),
            new MenuItemViewModel("Admin", _repoAdminPageViewModelFactory.Create(_repo))
        ];
    }

    [RelayCommand]
    private async Task LoadProfiles(CancellationToken cancellationToken)
    {
        var profiles = await _profileService.GetProfiles(_repo.Id, cancellationToken);
        var viewModels = profiles.Select(x => new ProfileItemViewModel(x));

        Profiles.Clear();
        foreach (var profile in viewModels)
        {
            Profiles.Add(profile);
        }
    }
}
