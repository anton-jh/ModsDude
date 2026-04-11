using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Navigation;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class RepoPageViewModel
    : PageViewModel, IDisposable
{
    private readonly RepoModel _repo;
    private readonly RepoAdminPageViewModel.Factory _repoAdminPageViewModelFactory;
    private readonly CreateProfilePageViewModel.Factory _createProfilePageViewModelFactory;
    private readonly ProfilePageViewModel.Factory _profilePageViewModelFactory;
    private readonly ProfileService _profileService;
    private readonly CreateLocalInstancePageViewModel.Factory _createLocalInstancePageViewModelFactory;
    private readonly RepoModsPageViewModel.Factory _repoModsPageViewModelFactory;
    private readonly EditLocalInstancePageViewModel.Factory _editLocalInstancePageViewModelFactory;
    private readonly ObservableCollectionSynchronizer<ProfileDto, MenuItemViewModel, string> _profilesSynchronizer;
    private readonly ObservableCollectionSynchronizer<LocalInstance, MenuItemViewModel, string> _instanceSynchronizer;


    public RepoPageViewModel(
        RepoModel repo,
        RepoAdminPageViewModel.Factory repoAdminPageViewModelFactory,
        CreateProfilePageViewModel.Factory createProfilePageViewModelFactory,
        ProfilePageViewModel.Factory profilePageViewModelFactory,
        EditLocalInstancePageViewModel.Factory editLocalInstancePageViewModelFactory,
        CreateLocalInstancePageViewModel.Factory createLocalInstancePageViewModelFactory,
        RepoModsPageViewModel.Factory repoModsPageViewModelFactory,
        ProfileService profileService,
        NavigationLockService navigationLockService,
        IModalService modalService,
        LocalInstanceService localInstanceService)
    {
        _repo = repo;
        _repoAdminPageViewModelFactory = repoAdminPageViewModelFactory;
        _createProfilePageViewModelFactory = createProfilePageViewModelFactory;
        _profilePageViewModelFactory = profilePageViewModelFactory;
        _profileService = profileService;
        _createLocalInstancePageViewModelFactory = createLocalInstancePageViewModelFactory;
        _repoModsPageViewModelFactory = repoModsPageViewModelFactory;
        _editLocalInstancePageViewModelFactory = editLocalInstancePageViewModelFactory;
        _name = repo.Name;

        var connectGameMenuItem = new MenuItemViewModel("Connect game", () => _createLocalInstancePageViewModelFactory.Create(repo));
        MenuItems = [
            new MenuItemViewModel("Overview", () => new ExamplePageViewModel(Name, "Overview")),
            new MenuItemViewModel("Admin", () => _repoAdminPageViewModelFactory.Create(_repo)),
            new MenuItemViewModel("Members", () => new ExamplePageViewModel(Name, "Members")),
            new MenuItemViewModel("Mods", () => _repoModsPageViewModelFactory.Create(repo)),
            new MenuItemViewModel("Create profile", () => _createProfilePageViewModelFactory.Create(repo)),
            connectGameMenuItem
        ];

        Instances = [];
        _instanceSynchronizer = new(localInstanceService.GetByRepoId(repo.Id), Instances, MapInstanceToVm, x => x.Title);

        Profiles = [];
        _profileService.ProfileOfInterestChanged += ProfileOfInterestChanged;
        _profilesSynchronizer = new(_profileService.Profiles, Profiles, MapProfileToVm, x => x.Title);

        NavManager = new(navigationLockService, modalService)
        {
            Selected = MenuItems.First()
        };

        if (Instances.Count == 0)
        {
            NavManager.Selected = connectGameMenuItem;
        }
    }


    [ObservableProperty]
    private string _name;

    public NavigationManager NavManager { get; }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public ObservableCollection<MenuItemViewModel> Profiles { get; }

    public ObservableCollection<MenuItemViewModel> Instances { get; }


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


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoPageViewModel Create(RepoModel repo)
        {
            return ActivatorUtilities.CreateInstance<RepoPageViewModel>(serviceProvider, repo);
        }
    }
}
