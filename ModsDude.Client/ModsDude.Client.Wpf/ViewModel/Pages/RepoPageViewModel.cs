using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class RepoPageViewModel
    : PageViewModel, IDisposable
{
    private readonly RepoModel _repo;
    private readonly RepoAdminPageViewModelFactory _repoAdminPageViewModelFactory;
    private readonly CreateProfilePageViewModelFactory _createProfilePageViewModelFactory;
    private readonly ProfilePageViewModelFactory _profilePageViewModelFactory;
    private readonly ProfileService _profileService;
    private readonly ObservableCollectionSynchronizer<ProfileDto, IMenuItemViewModel, string> _profilesSynchronizer;


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

        Instances = [
            new MenuItemViewModel("Game", new ExamplePageViewModel("Instance (Game)")),
            new MenuItemViewModel("Dedicated server", new ExamplePageViewModel("Instance (Dedicated server)"))
        ];

        Profiles = [];

        NavManager = new()
        {
            Selected = MenuItems.First()
        };

        _profileService.ProfileOfInterestChanged += ProfileOfInterestChanged;

        _profilesSynchronizer = new(_profileService.Profiles, Profiles, MapProfileToVm, x => x.Title);
    }


    [ObservableProperty]
    private string _name;

    public SidebarNavigationManager NavManager { get; }

    public ObservableCollection<IMenuItemViewModel> MenuItems { get; }

    public ObservableCollection<IMenuItemViewModel> Profiles { get; }

    public ObservableCollection<IMenuItemViewModel> Instances { get; }


    public override void Init()
    {
        LoadProfilesCommand.Execute(null);
    }

    public void Dispose()
    {
        _profilesSynchronizer.Dispose();
        NavManager.Dispose();
    }


    [RelayCommand]
    private async Task LoadProfiles(CancellationToken cancellationToken)
    {
        await _profileService.RefreshProfiles(_repo.Id, cancellationToken);
    }

    private async void ProfileOfInterestChanged(Guid profileIdOfInterest)
    {
        await LoadProfiles(default);

        var repo = Profiles
            .OfType<ProfileItemViewModel>()
            .FirstOrDefault(x => x.Id == profileIdOfInterest);

        NavManager.Selected = repo;
    }

    private ProfileItemViewModel MapProfileToVm(ProfileDto profile)
    {
        return new ProfileItemViewModel(profile, _profilePageViewModelFactory);
    }
}
