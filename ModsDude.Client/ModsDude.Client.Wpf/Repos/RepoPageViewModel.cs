using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Games;
using ModsDude.Client.Wpf.Mods;
using ModsDude.Client.Wpf.Profiles;
using ModsDude.Client.Wpf.Repos.Archive;
using ModsDude.Client.Wpf.Repos.Members;
using ModsDude.Client.Wpf.Savegames;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Sidebar;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.Repos;
public partial class RepoPageViewModel
    : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly RepoAdminPageViewModel.Factory _repoAdminPageViewModelFactory;
    private readonly CreateProfilePageViewModel.Factory _createProfilePageViewModelFactory;
    private readonly ProfilePageViewModel.Factory _profilePageViewModelFactory;
    private readonly IProfileService _profileService;
    private readonly ILastSelectionRepository _lastSelectionRepository;
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
    private readonly IProfileSyncStatusService _syncStatus;

    /// <summary>The Overview entry, kept so the header's repo name can take the user back to it.</summary>
    private readonly MenuItemViewModel _overviewMenuItem;

    /// <summary>
    /// Create profile, which is a page like any other but is reached from the "+" on the Profiles
    /// header rather than from the menu - so it is held here, not in <see cref="MenuItems"/>.
    /// </summary>
    private readonly MenuItemViewModel _createProfileMenuItem;

    /// <summary>Which row the Archive should pick out on arrival. One-shot, like the others.</summary>
    private Guid? _highlightInArchiveOnce;

    /// <summary>Which savegame the Saves list should arrive with selected. One-shot, like the others.</summary>
    private Guid? _selectSavegameOnce;

    private readonly ObservableCollectionSynchronizer<ProfileDto, MenuItemViewModel, string> _profilesSynchronizer;

    /// <summary>
    /// Connect game and Configure game: sub-pages of Overview, reached from its "This machine" card.
    /// Only a game whose adapter has local settings has either.
    /// </summary>
    private readonly MenuItemViewModel _connectGameItem;
    private readonly MenuItemViewModel _configureGameItem;


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
        IProfileSyncStatusService syncStatus,
        IProfileService profileService,
        ILastSelectionRepository lastSelectionRepository,
        IGameRepository gameRepository,
        INavigationLockService navigationLockService,
        IModalService modalService)
    {
        // A game installed since the repo list was last read is picked up on opening the repo rather
        // than on the next refresh. Quietly where it is still not there: the overview says so.
        try
        {
            gameRepository.ConnectAutomatically(repo.Adapter);
        }
        catch (UserFriendlyException)
        {
        }

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
        NavManager = new(navigationLockService, modalService);

        // Every entry whose page is gated end to end is closed here rather than left to fail at the
        // server. Mods is absent from this list on purpose: a guest can read the catalog, and only
        // the actions on it are refused - see RepoModsPageViewModel.
        var isGuest = repo.MembershipLevel < RepoMembershipLevel.Member;
        var isNotAdmin = repo.MembershipLevel < RepoMembershipLevel.Admin;

        var overviewLinks = new RepoOverviewLinks(ConnectGame, ConfigureGame);

        _overviewMenuItem = new MenuItemViewModel("Overview", () => repoOverviewPageViewModelFactory.Create(repo, overviewLinks))
            .WithIcon(MenuIcons.Overview);

        _connectGameItem = new MenuItemViewModel("Connect game", () => _connectGamePageViewModelFactory.Create(repo, NavManager.GoBackCommand))
            .Under(_overviewMenuItem);

        // Falls back to Connect game rather than asserting: the game can be disconnected between the
        // click and the page being built, and the shell must not fall over on that race.
        _configureGameItem = new MenuItemViewModel("Configure game", () => ConnectedGame() is Game game
            ? _gameSettingsPageViewModelFactory.Create(_repo, game, NavManager.GoBackCommand)
            : _connectGamePageViewModelFactory.Create(_repo, NavManager.GoBackCommand))
            .Under(_overviewMenuItem);

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
                var select = _selectSavegameOnce;
                _selectSavegameOnce = null;

                return repoSavegamesPageViewModelFactory.Create(repo, select);
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

        // A repo with nothing connected is a repo nothing works in, so being pushed at the one thing
        // that fixes that beats landing on an overview describing it - where there is anything to
        // do about it here. A game that connects by itself has no connect page, and the overview is
        // where it says it was not found.
        NavManager.Selected = NeedsConnecting() ? _connectGameItem : _overviewMenuItem;

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
    /// its sync state off this.
    /// </summary>
    public ProfilePageViewModel? OpenProfile => NavManager.CurrentPage as ProfilePageViewModel;

    public bool HasOpenProfile => OpenProfile is not null;

    /// <summary>
    /// Whether the header offers to connect a game: only while none is, and not on the page that does
    /// it.
    /// </summary>
    public bool ShowConnectGame => NeedsConnecting()
        && ReferenceEquals(NavManager.Current, _connectGameItem) is false;

    /// <summary>Whether the Create profile page is showing, for the "+" to draw as selected.</summary>
    public bool IsCreateProfileSelected => ReferenceEquals(NavManager.Selected, _createProfileMenuItem);

    /// <summary>
    /// Carries the availability and the reason for the "+", so the membership rule stays where it was.
    /// </summary>
    public MenuItemViewModel CreateProfileItem => _createProfileMenuItem;

    /// <summary>
    /// Whether the server has profile changes the list does not show yet. Brought in by the refresh
    /// button and nothing else - see <see cref="Shared.RemoteChangeWatcher"/>.
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

    [RelayCommand]
    private void ConnectGame()
    {
        if (NeedsConnecting())
        {
            NavManager.Selected = _connectGameItem;
        }
    }

    private void ConfigureGame()
    {
        if (ConnectedGame() is not null && ConnectsAutomatically() is false)
        {
            NavManager.Selected = _configureGameItem;
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
    /// <param name="select">
    /// The savegame to arrive with selected. Set by a link about one savegame - a notice, a toast, a
    /// profile's history - which would otherwise land on whichever row happens to be first.
    /// </param>
    /// <returns>False where this repo has no savegames, or navigation was refused.</returns>
    public bool TrySelectSavegames(Guid? select = null)
    {
        if (_savesMenuItem is null)
        {
            return false;
        }

        // Read and cleared by the menu item's factory, so it applies to the page this call opens and
        // not to the next one somebody reaches through the sidebar.
        _selectSavegameOnce = select;

        if (ReferenceEquals(NavManager.Current, _savesMenuItem) is false)
        {
            NavManager.Selected = _savesMenuItem;
        }
        else
        {
            // Already open, so selecting it again constructs nothing and the factory never runs. Tell
            // the page the user is looking at instead.
            _selectSavegameOnce = null;

            if (NavManager.CurrentPage is RepoSavegamesPageViewModel page && select is Guid savegameId)
            {
                page.Select(savegameId);
            }
        }

        var selected = ReferenceEquals(NavManager.Current, _savesMenuItem);

        if (selected is false)
        {
            // Refused, so nothing read the value and it must not be waiting for whoever opens Saves
            // next.
            _selectSavegameOnce = null;
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
            : TrySelectSavegames(select: savegameId);
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

        if (ReferenceEquals(NavManager.Current, entry) is false)
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

        if (ReferenceEquals(NavManager.Current, _archiveMenuItem) is false)
        {
            NavManager.Selected = _archiveMenuItem;
        }

        var selected = ReferenceEquals(NavManager.Current, _archiveMenuItem);

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

    /// <inheritdoc cref="GameRepository.ConnectsAutomatically"/>
    /// <remarks>
    /// Asked of the adapter each time rather than kept, since the adapter is replaced whenever the
    /// repo's base settings are.
    /// </remarks>
    private bool ConnectsAutomatically() => GameRepository.ConnectsAutomatically(_repo.Adapter);

    private bool NeedsConnecting() => ConnectedGame() is null && ConnectsAutomatically() is false;

    /// <summary>
    /// Leaves Connect game once a game is connected, and Configure game once it is gone: either page
    /// would be about a state the user is no longer in. The Overview says what happened.
    /// </summary>
    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var connected = ConnectedGame() is not null;

        if ((connected && ReferenceEquals(NavManager.Current, _connectGameItem))
            || (connected is false && ReferenceEquals(NavManager.Current, _configureGameItem)))
        {
            NavManager.GoBackCommand.Execute(null);
        }

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
