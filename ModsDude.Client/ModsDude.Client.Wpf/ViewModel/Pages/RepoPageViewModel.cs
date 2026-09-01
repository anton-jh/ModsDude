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
using System.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;
public partial class RepoPageViewModel
    : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly RepoAdminPageViewModel.Factory _repoAdminPageViewModelFactory;
    private readonly CreateProfilePageViewModel.Factory _createProfilePageViewModelFactory;
    private readonly ProfilePageViewModel.Factory _profilePageViewModelFactory;
    private readonly ProfileService _profileService;
    private readonly LastSelectionRepository _lastSelectionRepository;
    private readonly CreateLocalInstancePageViewModel.Factory _createLocalInstancePageViewModelFactory;
    private readonly RepoModsPageViewModel.Factory _repoModsPageViewModelFactory;
    private readonly InstancePageViewModel.Factory _instancePageViewModelFactory;
    private readonly ObservableCollectionSynchronizer<ProfileDto, MenuItemViewModel, string> _profilesSynchronizer;
    private readonly ObservableCollectionSynchronizer<LocalInstance, MenuItemViewModel, string> _instanceSynchronizer;

    private bool _selectionRestored;


    public RepoPageViewModel(
        Repo repo,
        RepoAdminPageViewModel.Factory repoAdminPageViewModelFactory,
        RepoOverviewPageViewModel.Factory repoOverviewPageViewModelFactory,
        RepoMembersPageViewModel.Factory repoMembersPageViewModelFactory,
        CreateProfilePageViewModel.Factory createProfilePageViewModelFactory,
        ProfilePageViewModel.Factory profilePageViewModelFactory,
        InstancePageViewModel.Factory instancePageViewModelFactory,
        CreateLocalInstancePageViewModel.Factory createLocalInstancePageViewModelFactory,
        RepoModsPageViewModel.Factory repoModsPageViewModelFactory,
        ProfileService profileService,
        LastSelectionRepository lastSelectionRepository,
        NavigationLockService navigationLockService,
        IModalService modalService)
    {
        _repo = repo;
        _repoAdminPageViewModelFactory = repoAdminPageViewModelFactory;
        _createProfilePageViewModelFactory = createProfilePageViewModelFactory;
        _profilePageViewModelFactory = profilePageViewModelFactory;
        _profileService = profileService;
        _lastSelectionRepository = lastSelectionRepository;
        _createLocalInstancePageViewModelFactory = createLocalInstancePageViewModelFactory;
        _repoModsPageViewModelFactory = repoModsPageViewModelFactory;
        _instancePageViewModelFactory = instancePageViewModelFactory;

        var connectGameMenuItem = new MenuItemViewModel("Connect game", () => _createLocalInstancePageViewModelFactory.Create(repo));

        // Every entry whose page is gated end to end is closed here rather than left to fail at the
        // server. Mods is absent from this list on purpose: a guest can read the catalog, and only
        // the actions on it are refused - see RepoModsPageViewModel.
        var isGuest = repo.MembershipLevel < RepoMembershipLevel.Member;
        var isNotAdmin = repo.MembershipLevel < RepoMembershipLevel.Admin;

        MenuItems = [
            new MenuItemViewModel("Overview", () => repoOverviewPageViewModelFactory.Create(repo)),
            new MenuItemViewModel("Admin", () => _repoAdminPageViewModelFactory.Create(_repo))
                .RestrictIf(isNotAdmin, "Only an admin can rename this repo, change its game settings or delete it."),
            new MenuItemViewModel("Members", () => repoMembersPageViewModelFactory.Create(repo))
                .RestrictIf(isGuest, "Guests cannot see who else is in a repo, or invite anybody to it. Ask an admin for a higher membership level."),
            new MenuItemViewModel("Mods", () => _repoModsPageViewModelFactory.Create(repo)),
            new MenuItemViewModel("Create profile", () => _createProfilePageViewModelFactory.Create(repo))
                .RestrictIf(isGuest, "Guests cannot create profiles. Ask an admin for a higher membership level."),
            connectGameMenuItem
        ];

        Instances = [];
        _instanceSynchronizer = new(repo.LocalInstances, Instances, MapInstanceToVm, x => x.Title);

        Profiles = [];
        _profileService.ProfileCreated += OnProfileCreated;
        _profileService.ProfileUpdated += OnProfileUpdated;
        _profilesSynchronizer = new(_profileService.Profiles, Profiles, MapProfileToVm, x => x.Title);

        NavManager = new(navigationLockService, modalService)
        {
            Selected = MenuItems.First()
        };

        if (Instances.Count == 0)
        {
            NavManager.Selected = connectGameMenuItem;
        }

        NavManager.PropertyChanged += OnNavigationChanged;
    }


    public NavigationManager NavManager { get; }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public ObservableCollection<MenuItemViewModel> Profiles { get; }

    public ObservableCollection<MenuItemViewModel> Instances { get; }


    protected override void Init()
    {
        LoadProfilesCommand.Execute(null);
    }

    public void Dispose()
    {
        _profileService.ProfileCreated -= OnProfileCreated;
        _profileService.ProfileUpdated -= OnProfileUpdated;
        NavManager.PropertyChanged -= OnNavigationChanged;

        _profilesSynchronizer.Dispose();
        _instanceSynchronizer.Dispose();
        NavManager.Dispose();
    }


    [RelayCommand]
    private async Task LoadProfiles(CancellationToken cancellationToken)
    {
        await _profileService.RefreshProfiles(_repo.Id, cancellationToken);

        RestoreLastSelectedProfile();
    }

    /// <summary>
    /// Selects a profile and hands back the page it opened, for a deep link from outside the sidebar.
    /// </summary>
    /// <returns>
    /// Null where the profile is gone, or where the page in front of the user refused to be navigated
    /// away from.
    /// </returns>
    public async Task<ProfilePageViewModel?> TrySelectProfileAsync(Guid profileId)
    {
        // A repo opened a moment ago has its profile list still on the way, so a deep link arriving
        // first has to wait for it rather than concluding the profile does not exist.
        if (FindProfile(profileId) is null)
        {
            await LoadProfilesCommand.ExecuteAsync(null);
        }

        if (FindProfile(profileId) is not ProfileItemViewModel entry)
        {
            return null;
        }

        if (ReferenceEquals(NavManager.Selected, entry) is false)
        {
            NavManager.Selected = entry;
        }

        return NavManager.CurrentPage as ProfilePageViewModel;
    }

    /// <summary>
    /// Only on the first load, and only once the repo is actually usable - being pushed at "Connect
    /// game" matters more than coming back to where you were.
    /// </summary>
    private void RestoreLastSelectedProfile()
    {
        if (_selectionRestored)
        {
            return;
        }

        _selectionRestored = true;

        if (Instances.Count == 0)
        {
            return;
        }

        var entries = Profiles.OfType<ProfileItemViewModel>().ToList();

        if (_lastSelectionRepository.GetLastProfile(entries.Select(x => x.Id)) is not Guid profileId)
        {
            return;
        }

        NavManager.Selected = entries.First(x => x.Id == profileId);
    }

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationManager.Selected) &&
            NavManager.Selected is ProfileItemViewModel profile)
        {
            _lastSelectionRepository.RecordProfile(profile.Id);
        }
    }

    private void OnProfileCreated(Guid profileId)
    {
        if (Profiles.OfType<ProfileItemViewModel>().FirstOrDefault(x => x.Id == profileId) is ProfileItemViewModel profile)
        {
            NavManager.Selected = profile;
        }
    }

    private void OnProfileUpdated(Guid profileId)
    {
        foreach (var profile in Profiles.OfType<ProfileItemViewModel>().Where(x => x.Id == profileId))
        {
            profile.RefreshTitle();
        }
    }

    private ProfileItemViewModel? FindProfile(Guid profileId)
        => Profiles.OfType<ProfileItemViewModel>().FirstOrDefault(x => x.Id == profileId);

    private ProfileItemViewModel MapProfileToVm(ProfileDto profile)
    {
        return new ProfileItemViewModel(_repo, profile, _profilePageViewModelFactory);
    }

    private InstanceItemViewModel MapInstanceToVm(LocalInstance instance)
    {
        return new InstanceItemViewModel(_repo, instance, _instancePageViewModelFactory);
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoPageViewModel Create(Repo repo)
        {
            return ActivatorUtilities.CreateInstance<RepoPageViewModel>(serviceProvider, repo);
        }
    }
}
