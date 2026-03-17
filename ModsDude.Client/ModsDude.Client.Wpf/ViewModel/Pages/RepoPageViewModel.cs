using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class RepoPageViewModel
    : PageViewModel
{
    private readonly RepoModel _repo;
    private readonly RepoAdminPageViewModelFactory _repoAdminPageViewModelFactory;
    private readonly CreateProfilePageViewModelFactory _createProfilePageViewModelFactory;
    private readonly ProfilePageViewModelFactory _profilePageViewModelFactory;
    private readonly ProfileService _profileService;


    public RepoPageViewModel(
        RepoModel repo,
        RepoAdminPageViewModelFactory repoAdminPageViewModelFactory,
        CreateProfilePageViewModelFactory createProfilePageViewModelFactory,
        ProfilePageViewModelFactory profilePageViewModelFactory,
        ProfileService profileService)
    {
        _repo = repo;
        _repoAdminPageViewModelFactory = repoAdminPageViewModelFactory;
        _createProfilePageViewModelFactory = createProfilePageViewModelFactory;
        _profilePageViewModelFactory = profilePageViewModelFactory;
        _profileService = profileService;
        _name = repo.Name;

        MenuItems = [
            new MenuItemViewModel("Overview", new ExamplePageViewModel($"Repo overview ({Name})")),
            new MenuItemViewModel("Admin", _repoAdminPageViewModelFactory.Create(_repo)),
            new MenuItemViewModel("Create profile", () => _createProfilePageViewModelFactory.Create(repo))
        ];
        _selectedMenuItem = MenuItems.First();

        _profileService.ProfileListChanged += ProfileListChanged;
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


    [RelayCommand]
    private async Task LoadProfiles(CancellationToken cancellationToken)
    {
        var profiles = await _profileService.GetProfiles(_repo.Id, cancellationToken);
        var viewModels = profiles.Select(x => new ProfileItemViewModel(x, _profilePageViewModelFactory));

        Profiles.Clear();
        foreach (var profile in viewModels)
        {
            Profiles.Add(profile);
        }
    }

    private async void ProfileListChanged(Guid? profileIdOfInterest)
    {
        await LoadProfiles(default);

        if (profileIdOfInterest is not null)
        {
            var repo = Profiles
                .OfType<ProfileItemViewModel>()
                .FirstOrDefault(x => x.Id == profileIdOfInterest);

            SelectedMenuItem = repo;
        }
    }
}
