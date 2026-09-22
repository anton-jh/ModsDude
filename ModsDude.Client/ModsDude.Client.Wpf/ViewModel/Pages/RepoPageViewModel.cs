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
using System.Collections.Specialized;
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
    private readonly ConnectGamePageViewModel.Factory _connectGamePageViewModelFactory;
    private readonly RepoModsPageViewModel.Factory _repoModsPageViewModelFactory;
    private readonly GameSettingsPageViewModel.Factory _gameSettingsPageViewModelFactory;
    /// <summary>
    /// The Saves entry, kept so a deep link can select it - a blocked prune names the savegame
    /// snapshots holding a revision, and a link that could not open the list would be no link at all.
    /// Null for a game with no savegames, where there is no entry to select.
    /// </summary>
    private readonly MenuItemViewModel? _savesMenuItem;
    private readonly MenuItemViewModel _archiveMenuItem;
    private readonly ISavegamesClient _savegamesClient;
    private readonly ProfileSyncStatusService _syncStatus;

    /// <summary>The Overview entry, kept so the header's repo name can take the user back to it.</summary>
    private readonly MenuItemViewModel _overviewMenuItem;

    /// <summary>
    /// Create profile, which is a page like any other but is reached from the "+" on the Profiles
    /// header rather than from the menu - so it is held here, not in <see cref="MenuItems"/>.
    /// </summary>
    private readonly MenuItemViewModel _createProfileMenuItem;

    /// <summary>Which row the Archive should pick out on arrival. One-shot, like the others.</summary>
    private Guid? _highlightInArchiveOnce;

    /// <summary>
    /// Whether the Saves list should arrive with past savegames showing. One-shot, like the others: it
    /// describes an arrival rather than a standing preference, so opening Saves from the sidebar
    /// afterwards gets the default back.
    /// </summary>
    private bool _showPastSavegamesOnce;

    private readonly ObservableCollectionSynchronizer<ProfileDto, MenuItemViewModel, string> _profilesSynchronizer;

    /// <summary>
    /// The two entries at the bottom of the menu, exactly one of which is in it at a time: the
    /// settings of the game this machine has connected for this repo, or the invitation to connect
    /// one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no game list any more.</b> A game is keyed by its identity and a repo is about one
    /// game, so a repo offers at most one - which makes a list of them a list that is always empty or
    /// always one long, under a heading saying "Games".
    /// </para>
    /// <para>
    /// <b>And no game page behind it either.</b> The entry used to be titled with the game's own
    /// name and to open a shell of its own over Sync, Saves and Manage - a proper noun sitting in a
    /// list of nouns-of-function, which is what made a local installation read as a fourth kind of
    /// entity beside repos and profiles. It is settings now, named for what it holds and nothing
    /// else: <em>Configure game</em>, the folders this machine points at. Where the game stands -
    /// which profile it follows, what it is holding, how far each folder has drifted - is on the
    /// repo's Overview, and what to do about it is on a profile's page or the app-level notice.
    /// </para>
    /// </remarks>
    private readonly MenuItemViewModel _connectGameMenuItem;
    private readonly MenuItemViewModel _gameMenuItem;

    private bool _selectionRestored;


    public RepoPageViewModel(
        Repo repo,
        RepoAdminPageViewModel.Factory repoAdminPageViewModelFactory,
        RepoOverviewPageViewModel.Factory repoOverviewPageViewModelFactory,
        RepoMembersPageViewModel.Factory repoMembersPageViewModelFactory,
        CreateProfilePageViewModel.Factory createProfilePageViewModelFactory,
        ProfilePageViewModel.Factory profilePageViewModelFactory,
        GameSettingsPageViewModel.Factory gameSettingsPageViewModelFactory,
        ConnectGamePageViewModel.Factory connectGamePageViewModelFactory,
        RepoModsPageViewModel.Factory repoModsPageViewModelFactory,
        RepoSavegamesPageViewModel.Factory repoSavegamesPageViewModelFactory,
        RepoArchivePageViewModel.Factory repoArchivePageViewModelFactory,
        ISavegamesClient savegamesClient,
        ProfileSyncStatusService syncStatus,
        ProfileService profileService,
        LastSelectionRepository lastSelectionRepository,
        NavigationLockService navigationLockService,
        IModalService modalService)
    {
        _repo = repo;
        _savegamesClient = savegamesClient;
        _syncStatus = syncStatus;
        _repoAdminPageViewModelFactory = repoAdminPageViewModelFactory;
        _createProfilePageViewModelFactory = createProfilePageViewModelFactory;
        _profilePageViewModelFactory = profilePageViewModelFactory;
        _profileService = profileService;
        _lastSelectionRepository = lastSelectionRepository;
        _connectGamePageViewModelFactory = connectGamePageViewModelFactory;
        _repoModsPageViewModelFactory = repoModsPageViewModelFactory;
        _gameSettingsPageViewModelFactory = gameSettingsPageViewModelFactory;

        _connectGameMenuItem = new MenuItemViewModel("Connect game", () => _connectGamePageViewModelFactory.Create(repo))
            .WithIcon(MenuIcons.ConnectGame);

        // Titled for what it holds rather than for the game, which is the whole of the entry's job
        // now: the game it acts on is whichever one the repo offers at the time it is clicked, and a
        // repo offers at most one, so there is nothing to pick between and nothing to re-title. It
        // falls back to Connect game rather than asserting: the entry is only in the menu while
        // there is a game, but nothing stops a deep link setting the selection to it, and the shell
        // must not fall over on a race with a disconnect.
        _gameMenuItem = new MenuItemViewModel("Configure game", () => ConnectedGame() is Game game
            ? _gameSettingsPageViewModelFactory.Create(_repo, game)
            : _connectGamePageViewModelFactory.Create(_repo))
            .WithIcon(MenuIcons.Game);

        // Every entry whose page is gated end to end is closed here rather than left to fail at the
        // server. Mods is absent from this list on purpose: a guest can read the catalog, and only
        // the actions on it are refused - see RepoModsPageViewModel.
        var isGuest = repo.MembershipLevel < RepoMembershipLevel.Member;
        var isNotAdmin = repo.MembershipLevel < RepoMembershipLevel.Admin;

        _overviewMenuItem = new MenuItemViewModel("Overview", () => repoOverviewPageViewModelFactory.Create(repo))
            .WithIcon(MenuIcons.Overview);

        MenuItems = [
            _overviewMenuItem,
            new MenuItemViewModel("Admin", () => _repoAdminPageViewModelFactory.Create(_repo))
                .WithIcon(MenuIcons.Admin)
                .RestrictIf(isNotAdmin, "Only an admin can rename this repo, change its game settings or delete it."),
            new MenuItemViewModel("Members", () => repoMembersPageViewModelFactory.Create(repo))
                .WithIcon(MenuIcons.Members)
                .RestrictIf(isGuest, "Guests cannot see who else is in a repo, or invite anybody to it. Ask an admin for a higher membership level."),
            new MenuItemViewModel("Mods", () => _repoModsPageViewModelFactory.Create(repo))
                .WithIcon(MenuIcons.Mods)
        ];

        // Saves is the sibling of Mods and sits next to it, and is *absent* rather than closed where
        // the adapter has no savegames - exactly as Mods would be for an adapter with no mods. That is
        // the distinction between a restriction and a capability: a level is something to ask an admin
        // for, and a game that has no savegames is not.
        if (repo.Adapter.CanSupportSavegames)
        {
            _savesMenuItem = new MenuItemViewModel("Saves", () =>
            {
                var showPastSavegames = _showPastSavegamesOnce;
                _showPastSavegamesOnce = false;

                return repoSavegamesPageViewModelFactory.Create(repo, showPastSavegames);
            }).WithIcon(MenuIcons.Saves);

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
        }).WithIcon(MenuIcons.Archive);

        MenuItems.Add(_archiveMenuItem);

        // Not in the menu: it is an act on the list below it rather than a place, so it lives as a "+"
        // on that list's header. It is still an entry - selecting it is how the page opens and how the
        // header knows to draw the button as selected - and it keeps the membership rule it had.
        _createProfileMenuItem = new MenuItemViewModel("Create profile", () => _createProfilePageViewModelFactory.Create(repo))
            .WithIcon(MenuIcons.CreateProfile)
            .RestrictIf(isGuest, "Guests cannot create profiles. Ask an admin for a higher membership level.");

        Profiles = [];
        _profileService.ProfileCreated += OnProfileCreated;
        _profileService.ProfileUpdated += OnProfileUpdated;
        _profileService.PendingChangesChanged += OnPendingProfileChangesChanged;
        _profilesSynchronizer = new(_profileService.Profiles, Profiles, MapProfileToVm, x => x.Title, NaturalOrder.Comparer);

        NavManager = new(navigationLockService, modalService)
        {
            Selected = MenuItems.First()
        };

        // Before the selection below and after the manager exists, because it moves the selection
        // when the entry under it leaves the menu.
        RefreshGameEntry();

        // A repo with nothing connected is a repo nothing works in, so being pushed at the one thing
        // that fixes that beats landing on an overview describing it.
        if (ConnectedGame() is null)
        {
            NavManager.Selected = _connectGameMenuItem;
        }

        _repo.Games.CollectionChanged += OnGamesChanged;
        _repo.PropertyChanged += OnRepoChanged;
        _syncStatus.Changed += OnSyncStatusChanged;
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


    // What the header draws. It is here rather than on a page because it spans the repo's own pages and
    // a profile's, and is the one thing on screen that names both.

    public string RepoName => _repo.Name;

    public string GameName => _repo.Adapter.GameDisplayName;

    /// <summary>
    /// The profile page in front of the user, or null on any of the repo's own pages. The header reads
    /// its name, its sync state and its activation control off this, so there is one activation
    /// control however deep in the profile the user is.
    /// </summary>
    public ProfilePageViewModel? OpenProfile => NavManager.CurrentPage as ProfilePageViewModel;

    public bool HasOpenProfile => OpenProfile is not null;

    /// <summary>
    /// Whether this sidebar is a rail: a profile's own sidebar is the deepest one there is while one is
    /// open, so this is what is left of the way to it. Opening it again is a hover away.
    /// </summary>
    public bool IsSidebarCollapsed => HasOpenProfile;

    /// <summary>
    /// Whether the header offers to connect a game: only while none is, and not on the page that does
    /// it.
    /// </summary>
    public bool ShowConnectGame => ConnectedGame() is null && ReferenceEquals(NavManager.Selected, _connectGameMenuItem) is false;

    /// <summary>Whether the Create profile page is showing, for the "+" to draw as selected.</summary>
    public bool IsCreateProfileSelected => ReferenceEquals(NavManager.Selected, _createProfileMenuItem);

    /// <summary>
    /// Carries the availability and the reason for the "+", so the membership rule stays where it was.
    /// </summary>
    public MenuItemViewModel CreateProfileItem => _createProfileMenuItem;

    /// <summary>
    /// Whether the server has profile changes the list does not show yet. Brought in by the refresh
    /// button and nothing else - see <see cref="Wpf.Services.RemoteChangeWatcher"/>.
    /// </summary>
    public bool HasPendingProfileChanges => PendingProfileChanges() is not null;

    /// <summary>The refresh button's tooltip, which says what pressing it would bring in when it knows.</summary>
    public string RefreshProfilesToolTip => PendingProfileChanges() is { } changes
        ? $"{changes.Describe()}{Environment.NewLine}{Environment.NewLine}Refresh to bring the changes in."
        : "Refresh profiles";


    protected override void Init()
    {
        LoadProfilesCommand.Execute(null);
    }

    public void Dispose()
    {
        _profileService.ProfileCreated -= OnProfileCreated;
        _profileService.ProfileUpdated -= OnProfileUpdated;
        _profileService.PendingChangesChanged -= OnPendingProfileChangesChanged;
        _repo.Games.CollectionChanged -= OnGamesChanged;
        _repo.PropertyChanged -= OnRepoChanged;
        _syncStatus.Changed -= OnSyncStatusChanged;
        NavManager.PropertyChanged -= OnNavigationChanged;

        _profilesSynchronizer.Dispose();
        NavManager.Dispose();
    }


    [RelayCommand]
    private async Task LoadProfiles(CancellationToken cancellationToken)
    {
        await _profileService.RefreshProfiles(_repo.Id, cancellationToken);
    }

    [RelayCommand]
    private void GoToOverview()
    {
        NavManager.Selected = _overviewMenuItem;
    }

    /// <summary>
    /// Takes the user to the Connect game entry, which is in the sidebar for exactly as long as no game
    /// is connected - so the page the header sends them to is always one the menu also names.
    /// </summary>
    [RelayCommand]
    private void ConnectGame()
    {
        if (ConnectedGame() is null)
        {
            NavManager.Selected = _connectGameMenuItem;
        }
    }

    [RelayCommand]
    private void CreateProfile()
    {
        NavManager.Selected = _createProfileMenuItem;
    }

    /// <summary>
    /// Selects the repo's Saves list.
    /// </summary>
    /// <param name="showPastSavegames">
    /// Whether to arrive with the past-savegames toggle on. Set by a link from a profile's count of them,
    /// which would otherwise land on a list filtering out the very rows it counted.
    /// </param>
    /// <returns>False where this repo has no savegames, or navigation was refused.</returns>
    public bool TrySelectSavegames(bool showPastSavegames = false)
    {
        if (_savesMenuItem is null)
        {
            return false;
        }

        // Read and cleared by the menu item's factory, so it applies to the page this call opens and
        // not to the next one somebody reaches through the sidebar.
        _showPastSavegamesOnce = showPastSavegames;

        if (ReferenceEquals(NavManager.Selected, _savesMenuItem) is false)
        {
            NavManager.Selected = _savesMenuItem;
        }
        else if (showPastSavegames)
        {
            // Already open, so selecting it again constructs nothing and the factory never runs. Turn
            // the toggle on for the page the user is looking at instead.
            _showPastSavegamesOnce = false;

            if (NavManager.CurrentPage is RepoSavegamesPageViewModel page)
            {
                page.ShowPastSavegames = true;
            }
        }

        var selected = ReferenceEquals(NavManager.Selected, _savesMenuItem);

        if (selected is false)
        {
            // Refused, so nothing read the value and it must not be waiting for whoever opens Saves
            // next.
            _showPastSavegamesOnce = false;
        }

        return selected;
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
    /// Which list it is in is asked of the server rather than guessed. The head-snapshot cache would
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
        entry.SyncState = _syncStatus.StateOf(_repo, entry.Id);

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

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationManager.CurrentPage))
        {
            OnPropertyChanged(nameof(OpenProfile));
            OnPropertyChanged(nameof(HasOpenProfile));
            OnPropertyChanged(nameof(IsSidebarCollapsed));

            return;
        }

        if (e.PropertyName != nameof(NavigationManager.Selected))
        {
            return;
        }

        OnPropertyChanged(nameof(ShowConnectGame));
        OnPropertyChanged(nameof(IsCreateProfileSelected));

        if (NavManager.Selected is ProfileItemViewModel profile)
        {
            _lastSelectionRepository.RecordProfile(profile.Id);
        }

        // The transient archived entry exists for one visit. Anything else being selected - including
        // the Archive itself, and including nothing at all - is the end of it.
        if (NavManager.Selected is not MenuItemViewModel selected || ArchivedProfiles.Contains(selected) is false)
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

    private void OnPendingProfileChangesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasPendingProfileChanges));
        OnPropertyChanged(nameof(RefreshProfilesToolTip));
    }

    /// <summary>
    /// The pending changes if they are this repo's. The service's list is handed from repo to repo, so
    /// for the moment between opening this one and its profiles arriving, they are the last repo's.
    /// </summary>
    private RemoteChanges? PendingProfileChanges()
        => _profileService.HeldRepoId == _repo.Id ? _profileService.PendingChanges : null;

    private ProfileItemViewModel? FindProfile(Guid profileId)
        => Profiles.OfType<ProfileItemViewModel>().FirstOrDefault(x => x.Id == profileId);

    private ProfileItemViewModel MapProfileToVm(ProfileDto profile)
    {
        var entry = new ProfileItemViewModel(_repo, profile, _profilePageViewModelFactory);
        entry.SyncState = _syncStatus.StateOf(_repo, entry.Id);

        return entry;
    }

    /// <summary>
    /// This machine's installation of the game this repo is about, or null where none is connected.
    /// </summary>
    /// <remarks>
    /// At most one by construction: a game is keyed by its identity, and <see cref="Repo.Games"/> is
    /// filtered to the identity this repo is about. <c>FirstOrDefault</c> rather than
    /// <c>SingleOrDefault</c> because a shell throwing on a state the model cannot produce is a crash
    /// where a blank sidebar would do.
    /// </remarks>
    private Game? ConnectedGame() => _repo.Games.FirstOrDefault();

    /// <summary>
    /// Puts exactly one of the two bottom entries in the menu: the connected game's settings, or the
    /// invitation to connect one.
    /// </summary>
    /// <remarks>
    /// Absent rather than closed, the same way the Saves entry is for an adapter with no savegames:
    /// "Connect game" on a repo that already has one, or a settings entry leading to a form about
    /// nothing, are both entries that describe a state the user is not in.
    /// </remarks>
    private void RefreshGameEntry()
    {
        var game = ConnectedGame();

        var wanted = game is null ? _connectGameMenuItem : _gameMenuItem;
        var unwanted = game is null ? _gameMenuItem : _connectGameMenuItem;

        // In before out, and the selection moved between them: a selection naming an entry the
        // bound list does not hold is one the ListView pushes straight back to null, so the entry
        // being selected has to be in the menu before it is selected and the one being dropped has
        // to be off the selection before it leaves.
        if (MenuItems.Contains(wanted) is false)
        {
            MenuItems.Add(wanted);
        }

        if (ReferenceEquals(NavManager.Selected, unwanted))
        {
            // Whatever was on screen is about a game that has just gone, or about connecting one
            // that has just arrived. Either way the page under it is about to stop making sense.
            NavManager.Selected = wanted;
        }

        MenuItems.Remove(unwanted);
    }

    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshGameEntry();
        OnPropertyChanged(nameof(ShowConnectGame));
    }

    private void OnRepoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Repo.Name))
        {
            OnPropertyChanged(nameof(RepoName));
        }
    }

    /// <summary>
    /// Re-asks the state of every profile row, because which one the game follows and whether its
    /// folders match can both change without a row being touched.
    /// </summary>
    private void OnSyncStatusChanged(object? sender, EventArgs e)
    {
        foreach (var profile in Profiles.Concat(ArchivedProfiles).OfType<ProfileItemViewModel>())
        {
            profile.SyncState = _syncStatus.StateOf(_repo, profile.Id);
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoPageViewModel Create(Repo repo)
        {
            return ActivatorUtilities.CreateInstance<RepoPageViewModel>(serviceProvider, repo);
        }
    }
}
