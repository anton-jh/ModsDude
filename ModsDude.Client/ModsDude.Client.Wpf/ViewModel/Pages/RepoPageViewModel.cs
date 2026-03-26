using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.Services;
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
    private readonly CreateLocalInstancePageViewModelFactory _createLocalInstancePageViewModelFactory;
    private readonly EditLocalInstancePageViewModelFactory _editLocalInstancePageViewModelFactory;
    private readonly ObservableCollectionSynchronizer<ProfileDto, IMenuItemViewModel, string> _profilesSynchronizer;
    private readonly ObservableCollectionSynchronizer<LocalInstance, IMenuItemViewModel, string> _instanceSynchronizer;


    public RepoPageViewModel(
        RepoModel repo,
        RepoAdminPageViewModelFactory repoAdminPageViewModelFactory,
        CreateProfilePageViewModelFactory createProfilePageViewModelFactory,
        ProfilePageViewModelFactory profilePageViewModelFactory,
        ProfileService profileService,
        NavigationLockService navigationLockService,
        IModalService modalService,
        CreateLocalInstancePageViewModelFactory createLocalInstancePageViewModelFactory,
        EditLocalInstancePageViewModelFactory editLocalInstancePageViewModelFactory)
    {
        _repo = repo;
        _repoAdminPageViewModelFactory = repoAdminPageViewModelFactory;
        _createProfilePageViewModelFactory = createProfilePageViewModelFactory;
        _profilePageViewModelFactory = profilePageViewModelFactory;
        _profileService = profileService;
        _createLocalInstancePageViewModelFactory = createLocalInstancePageViewModelFactory;
        _editLocalInstancePageViewModelFactory = editLocalInstancePageViewModelFactory;
        _name = repo.Name;

        MenuItems = [
            new MenuItemViewModel("Overview", new ExamplePageViewModel(Name, "Overview")),
            new MenuItemViewModel("Admin", () => _repoAdminPageViewModelFactory.Create(_repo)),
            new MenuItemViewModel("Members", new ExamplePageViewModel(Name, "Members")),
            new MenuItemViewModel("Mods", new ExamplePageViewModel(Name, "Mods")),
            new MenuItemViewModel("Create profile", () => _createProfilePageViewModelFactory.Create(repo)),
            new MenuItemViewModel("Connect game instance", () => _createLocalInstancePageViewModelFactory.Create(repo))
        ];

        Instances = [];
        _instanceSynchronizer = new(repo.LocalInstances, Instances, MapInstanceToVm, x => x.Title);

        Profiles = [];
        _profileService.ProfileOfInterestChanged += ProfileOfInterestChanged;
        _profilesSynchronizer = new(_profileService.Profiles, Profiles, MapProfileToVm, x => x.Title);

        NavManager = new(navigationLockService, modalService)
        {
            Selected = MenuItems.First()
        };
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
        _instanceSynchronizer.Dispose();
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
        return new ProfileItemViewModel(_repo, profile, _profilePageViewModelFactory);
    }

    private InstanceItemViewModel MapInstanceToVm(LocalInstance instance)
    {
        return new InstanceItemViewModel(_repo, instance, _editLocalInstancePageViewModelFactory);
    }
}
