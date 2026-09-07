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
    /// <summary>
    /// The Saves entry, kept so a deep link can select it - a blocked prune names the savegame
    /// versions holding a revision, and a link that could not open the list would be no link at all.
    /// Null for a game with no savegames, where there is no entry to select.
    /// </summary>
    private readonly MenuItemViewModel? _savesMenuItem;
    private readonly MenuItemViewModel _archiveMenuItem;
    private readonly ISavegamesClient _savegamesClient;

    /// <summary>Which row the Archive should pick out on arrival. One-shot, like the others.</summary>
    private Guid? _highlightInArchiveOnce;

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
        RepoSavegamesPageViewModel.Factory repoSavegamesPageViewModelFactory,
        RepoArchivePageViewModel.Factory repoArchivePageViewModelFactory,
        ISavegamesClient savegamesClient,
        ProfileService profileService,
        LastSelectionRepository lastSelectionRepository,
        NavigationLockService navigationLockService,
        IModalService modalService)
    {
        _repo = repo;
        _savegamesClient = savegamesClient;
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
            new MenuItemViewModel("Mods", () => _repoModsPageViewModelFactory.Create(repo))
        ];

        // Saves is the sibling of Mods and sits next to it, and is *absent* rather than closed where
        // the adapter has no savegames - exactly as Mods would be for an adapter with no mods. That is
        // the distinction between a restriction and a capability: a level is something to ask an admin
        // for, and a game that has no savegames is not.
        if (repo.Adapter.CanSupportSavegames)
        {
            _savesMenuItem = new MenuItemViewModel("Saves", () => repoSavegamesPageViewModelFactory.Create(repo));

            MenuItems.Add(_savesMenuItem);
        }

        // Open to everybody: a profile that quietly vanished from the sidebar has to be explainable
        // to whoever noticed, and only an admin can move anything in or out of it anyway.
        _archiveMenuItem = new MenuItemViewModel("Archive", () =>
        {
            var highlight = _highlightInArchiveOnce;
            _highlightInArchiveOnce = null;

            var page = repoArchivePageViewModelFactory.Create(repo);
            page.HighlightOnArrival(highlight);

            return page;
        });

        MenuItems.Add(_archiveMenuItem);

        MenuItems.Add(new MenuItemViewModel("Create profile", () => _createProfilePageViewModelFactory.Create(repo))
            .RestrictIf(isGuest, "Guests cannot create profiles. Ask an admin for a higher membership level."));
        MenuItems.Add(connectGameMenuItem);

        Instances = [];
        _instanceSynchronizer = new(repo.LocalInstances, Instances, MapInstanceToVm, x => x.Title, NaturalOrder.Comparer);

        Profiles = [];
        _profileService.ProfileCreated += OnProfileCreated;
        _profileService.ProfileUpdated += OnProfileUpdated;
        _profilesSynchronizer = new(_profileService.Profiles, Profiles, MapProfileToVm, x => x.Title, NaturalOrder.Comparer);

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

    /// <summary>
    /// At most one: an archived profile a link opened, shown under its own heading so that it reads
    /// as reached through the archive rather than as back in the list. Dropped on the next
    /// navigation.
    /// </summary>
    public ObservableCollection<MenuItemViewModel> ArchivedProfiles { get; } = [];

    public bool HasArchivedProfileOpen => ArchivedProfiles.Count > 0;

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
    /// Selects the repo's Saves list.
    /// </summary>
    /// <returns>False where this repo has no savegames, or navigation was refused.</returns>
    public bool TrySelectSavegames()
    {
        if (_savesMenuItem is null)
        {
            return false;
        }

        if (ReferenceEquals(NavManager.Selected, _savesMenuItem) is false)
        {
            NavManager.Selected = _savesMenuItem;
        }

        return ReferenceEquals(NavManager.Selected, _savesMenuItem);
    }

    /// <summary>
    /// Takes the user to one savegame - the saves list for a live one, the Archive with the row
    /// picked out for an archived one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A savegame has no page of its own; it is a row. So "take them to it" means the list it is in,
    /// and an archived one is only in the archive.
    /// </para>
    /// <para>
    /// Which list it is in is asked of the server rather than guessed. The head-version cache would
    /// have been free, but it is populated as a side effect of the saves page having been visited -
    /// so on a fresh window every savegame would look archived.
    /// </para>
    /// </remarks>
    public async Task<bool> TrySelectSavegameAsync(Guid savegameId)
    {
        var archived = false;

        try
        {
            archived = (await _savegamesClient.GetArchivedSavegamesV1Async(_repo.Id, CancellationToken.None))
                .Any(x => x.Id == savegameId);
        }
        catch (Exception)
        {
            // A link that cannot be followed does nothing, which is what it did before. Falling
            // through to the live list is the better guess of the two.
        }

        return archived
            ? TrySelectArchive(savegameId)
            : TrySelectSavegames();
    }

    /// <summary>
    /// Selects a profile and hands back the page it opened, for a deep link from outside the sidebar.
    /// Falls through to the archive for one that has been put away - see
    /// <see cref="OpenArchivedProfileAsync"/>.
    /// </summary>
    /// <returns>
    /// Null where the profile is gone entirely, or where the page in front of the user refused to be
    /// navigated away from.
    /// </returns>
    public async Task<ProfilePageViewModel?> TrySelectProfileAsync(Guid profileId)
    {
        // A repo opened a moment ago has its profile list still on the way, so a deep link arriving
        // first has to wait for it rather than concluding the profile does not exist.
        if (FindProfile(profileId) is null)
        {
            await LoadProfilesCommand.ExecuteAsync(null);
        }

        var entry = FindProfile(profileId) ?? await OpenArchivedProfileAsync(profileId);

        if (entry is null)
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
    /// Selects the repo's Archive, optionally with one row picked out.
    /// </summary>
    /// <returns>False where navigation was refused.</returns>
    public bool TrySelectArchive(Guid? highlight = null)
    {
        _highlightInArchiveOnce = highlight;

        if (ReferenceEquals(NavManager.Selected, _archiveMenuItem) is false)
        {
            NavManager.Selected = _archiveMenuItem;
        }

        var selected = ReferenceEquals(NavManager.Selected, _archiveMenuItem);

        if (selected is false)
        {
            _highlightInArchiveOnce = null;
        }

        return selected;
    }

    /// <summary>
    /// Puts an archived profile into the sidebar so its own pages can be opened, and hands back the
    /// entry to select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A link into an archived profile has to land on the profile.</b> Archiving takes it out of
    /// the lists, not out of existence - its revisions are still readable and still the answer to
    /// "which revision pins this mod" - so a refused delete naming revision 12 has to be able to
    /// show revision 12.
    /// </para>
    /// <para>
    /// <b>Its own collection, not the profile list.</b> The profile list is kept by a synchronizer
    /// over the live profiles; an entry pushed into it by hand is one the synchronizer never mapped
    /// and would therefore never remove, and restoring the profile would leave two entries for it.
    /// This one is transient, sits under its own heading so it reads as reached-through-the-archive
    /// rather than as back in the list, and is dropped the moment the user navigates elsewhere.
    /// </para>
    /// </remarks>
    private async Task<ProfileItemViewModel?> OpenArchivedProfileAsync(Guid profileId)
    {
        ProfileDto? archived;

        try
        {
            archived = (await _profileService.GetArchivedProfiles(_repo.Id, CancellationToken.None))
                .FirstOrDefault(x => x.Id == profileId);
        }
        catch (Exception)
        {
            // A link that cannot be followed is a link that does nothing, which is what it did
            // before. Nothing here is worth interrupting the user for.
            return null;
        }

        if (archived is null)
        {
            return null;
        }

        ClearArchivedProfile();

        var entry = new ProfileItemViewModel(_repo, archived, _profilePageViewModelFactory);

        ArchivedProfiles.Add(entry);
        OnPropertyChanged(nameof(HasArchivedProfileOpen));

        return entry;
    }

    /// <summary>
    /// Drops the transient entry once the user has gone somewhere else. It exists for one visit.
    /// </summary>
    private void ClearArchivedProfile()
    {
        if (ArchivedProfiles.Count == 0)
        {
            return;
        }

        ArchivedProfiles.Clear();
        OnPropertyChanged(nameof(HasArchivedProfileOpen));
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
        if (e.PropertyName != nameof(NavigationManager.Selected))
        {
            return;
        }

        if (NavManager.Selected is ProfileItemViewModel profile)
        {
            _lastSelectionRepository.RecordProfile(profile.Id);
        }

        // The transient archived entry exists for one visit. Anything else being selected - including
        // the Archive itself - is the end of it.
        if (ArchivedProfiles.Contains(NavManager.Selected) is false)
        {
            ClearArchivedProfile();
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
