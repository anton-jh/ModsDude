using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
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
using System.Windows;

namespace ModsDude.Client.Wpf.Repos;
public partial class RepoPageViewModel
    : PageViewModel, INavigationHost, IDisposable
{
    private readonly Repo _repo;
    private readonly CreateProfilePageViewModel.Factory _createProfilePageViewModelFactory;
    private readonly ProfilePageViewModel.Factory _profilePageViewModelFactory;
    private readonly IProfileStore _profileStore;
    private readonly IProfileService _profileService;
    private readonly ConnectGamePageViewModel.Factory _connectGamePageViewModelFactory;
    private readonly GameSettingsPageViewModel.Factory _gameSettingsPageViewModelFactory;
    private readonly ILogger<RepoPageViewModel> _logger;

    /// <summary>
    /// Every section entry in the menu, kept so a deep link can select one. A section the adapter has
    /// no capability for has no entry.
    /// </summary>
    private readonly IReadOnlyDictionary<RepoSection, MenuItemViewModel> _sections;
    private readonly ISavegameStore _savegames;
    private readonly ISavegameBindingStore _bindings;
    private readonly IProfileSyncStatusService _syncStatus;

    /// <summary>
    /// Create profile, which is a page like any other but is reached from the "+" on the Profiles
    /// header rather than from the menu - so it is held here, not in <see cref="MenuItems"/>.
    /// </summary>
    private readonly MenuItemViewModel _createProfileMenuItem;

    /// <summary>Which row the Archive should pick out on arrival. One-shot, like the others.</summary>
    private Guid? _highlightInArchiveOnce;

    /// <summary>Which savegame the Saves list should arrive with selected. One-shot, like the others.</summary>
    private Guid? _selectSavegameOnce;

    private readonly ObservableCollectionSynchronizer<Profile, MenuItemViewModel, string> _profilesSynchronizer;

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
        ISavegameStore savegames,
        ISavegameBindingStore bindings,
        IProfileSyncStatusService syncStatus,
        IProfileStore profileStore,
        IProfileService profileService,
        IGameRepository gameRepository,
        INavigationLockService navigationLockService,
        IModalService modalService,
        ILogger<RepoPageViewModel> logger)
    {
        _logger = logger;

        // A game installed since the repo list was last read is picked up on opening the repo rather
        // than on the next refresh. Quietly where it is still not there: the overview says so.
        try
        {
            gameRepository.ConnectAutomatically(repo.Adapter);
        }
        catch (UserFriendlyException exception)
        {
            _logger.LogInformation(exception, "Could not connect the game of repo {RepoId} on opening it.", repo.Id);
        }

        _repo = repo;
        _savegames = savegames;
        _bindings = bindings;
        _syncStatus = syncStatus;
        _createProfilePageViewModelFactory = createProfilePageViewModelFactory;
        _profilePageViewModelFactory = profilePageViewModelFactory;
        _profileStore = profileStore;
        _profileService = profileService;
        _connectGamePageViewModelFactory = connectGamePageViewModelFactory;
        _gameSettingsPageViewModelFactory = gameSettingsPageViewModelFactory;
        NavManager = new(navigationLockService, modalService);

        var isGuest = repo.MembershipLevel < RepoMembershipLevel.Member;

        var overviewLinks = new RepoOverviewLinks(ConnectGame, ConfigureGame);

        // The one-shot selections are read and cleared by the page they were set for, so they apply
        // to the page a deep link opens and not to the next one somebody reaches through the menu.
        PageViewModel CreateSectionPage(RepoSection section)
        {
            switch (section)
            {
                case RepoSection.Overview:
                    return repoOverviewPageViewModelFactory.Create(repo, overviewLinks);
                case RepoSection.Admin:
                    return repoAdminPageViewModelFactory.Create(repo);
                case RepoSection.Members:
                    return repoMembersPageViewModelFactory.Create(repo);
                case RepoSection.Mods:
                    return repoModsPageViewModelFactory.Create(repo);
                case RepoSection.Saves:
                    var select = _selectSavegameOnce;
                    _selectSavegameOnce = null;

                    return repoSavegamesPageViewModelFactory.Create(repo, select);
                case RepoSection.Archive:
                    var highlight = _highlightInArchiveOnce;
                    _highlightInArchiveOnce = null;

                    var archive = repoArchivePageViewModelFactory.Create(repo);
                    archive.HighlightOnArrival(highlight);

                    return archive;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section), section, null);
            }
        }

        var sections = RepoSections.Of(repo);

        _sections = sections.ToDictionary(
            x => x.Section,
            x =>
            {
                var item = new MenuItemViewModel(x.Title, () => CreateSectionPage(x.Section)).WithIcon(x.Icon);

                return x.RestrictedReason is string reason ? item.Restrict(reason) : item;
            });

        MenuItems = [.. sections.Select(x => _sections[x.Section])];

        _connectGameItem = new MenuItemViewModel("Connect game", () => _connectGamePageViewModelFactory.Create(repo, NavManager.GoBackCommand))
            .Under(OverviewItem);

        // Falls back to Connect game rather than asserting: the game can be disconnected between the
        // click and the page being built, and the shell must not fall over on that race.
        _configureGameItem = new MenuItemViewModel("Configure game", () => ConnectedGame() is Game game
            ? _gameSettingsPageViewModelFactory.Create(_repo, game, NavManager.GoBackCommand)
            : _connectGamePageViewModelFactory.Create(_repo, NavManager.GoBackCommand))
            .Under(OverviewItem);

        // Not in the menu: it is an act on the list below it rather than a place, so it lives as a "+"
        // on that list's header. It is still an entry - selecting it is how the page opens and how the
        // header knows to draw the button as selected - and it keeps the membership rule it had.
        _createProfileMenuItem = new MenuItemViewModel("Create profile", () => _createProfilePageViewModelFactory.Create(repo))
            .WithIcon(MenuIcons.CreateProfile)
            .RestrictIf(isGuest, "Guests cannot create profiles. Ask an admin for a higher membership level.");

        Profiles = [];
        _profileStore.ProfileCreated += OnProfileCreated;
        _profilesSynchronizer = new(_profileStore.Live(repo.Id), Profiles, MapProfileToVm, x => x.Title, NaturalOrder.Comparer);
        UpdateSavesBadge();

        // A repo with nothing connected is a repo nothing works in, so being pushed at the one thing
        // that fixes that beats landing on an overview describing it - where there is anything to
        // do about it here. A game that connects by itself has no connect page, and the overview is
        // where it says it was not found.
        NavManager.Selected = NeedsConnecting() ? _connectGameItem : OverviewItem;

        _repo.Games.CollectionChanged += OnGamesChanged;
        _repo.PropertyChanged += OnRepoChanged;
        _syncStatus.Changed += OnSyncStatusChanged;
        _bindings.BindingsChanged += OnBindingsChanged;
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


    protected override void Init()
    {
        LoadProfilesCommand.Execute(null);
    }

    public void Dispose()
    {
        _profileStore.ProfileCreated -= OnProfileCreated;
        _repo.Games.CollectionChanged -= OnGamesChanged;
        _repo.PropertyChanged -= OnRepoChanged;
        _syncStatus.Changed -= OnSyncStatusChanged;
        _bindings.BindingsChanged -= OnBindingsChanged;
        NavManager.PropertyChanged -= OnNavigationChanged;

        _profilesSynchronizer.Dispose();
        NavManager.Dispose();
    }


    [RelayCommand]
    private async Task LoadProfiles(CancellationToken cancellationToken)
    {
        await _profileStore.RefreshAsync(_repo.Id, cancellationToken);
    }

    [RelayCommand]
    private void GoToOverview()
    {
        NavManager.Selected = OverviewItem;
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

    /// <returns>
    /// False where the destination is absent or closed to this membership level, the profile is gone,
    /// or navigation was refused.
    /// </returns>
    public async Task<bool> TrySelectAsync(RepoDestination destination)
    {
        switch (destination)
        {
            case RepoDestination.Section { Kind: RepoSection.Saves }:
                return TrySelectSavegames(select: null);
            case RepoDestination.Section { Kind: RepoSection.Archive }:
                return TrySelectArchive(highlight: null);
            case RepoDestination.Section section:
                return _sections.GetValueOrDefault(section.Kind) is MenuItemViewModel item && TrySelect(item);
            case RepoDestination.ConnectGame:
                return NeedsConnecting() && TrySelect(_connectGameItem);
            case RepoDestination.Savegame savegame:
                return await TrySelectSavegameAsync(savegame.SavegameId);
            case RepoDestination.Profile profile:
                return await TrySelectProfileAsync(profile.ProfileId) is not null;
            case RepoDestination.ProfileMods mods:
                return await TrySelectProfileAsync(mods.ProfileId) is ProfilePageViewModel modsPage
                    && modsPage.TrySelectMods(mods.ScanTarget);
            case RepoDestination.ProfileHistory history:
                return await TrySelectProfileAsync(history.ProfileId) is ProfilePageViewModel historyPage
                    && historyPage.TrySelectHistory(history.Revision);
            default:
                throw new ArgumentOutOfRangeException(nameof(destination), destination, null);
        }
    }

    /// <summary>
    /// Selects an entry unless it is closed to this membership level. A closed entry refuses the
    /// click in the menu, and a deep link must not get past it either.
    /// </summary>
    private bool TrySelect(MenuItemViewModel item)
    {
        if (item.IsAvailable is false)
        {
            return false;
        }

        if (ReferenceEquals(NavManager.Current, item) is false)
        {
            NavManager.Selected = item;
        }

        return ReferenceEquals(NavManager.Current, item);
    }

    /// <param name="select">
    /// The savegame to arrive with selected. Set by a link about one savegame - a notice, a toast, a
    /// profile's history - which would otherwise land on whichever row happens to be first.
    /// </param>
    /// <returns>False where this repo has no savegames, or navigation was refused.</returns>
    private bool TrySelectSavegames(Guid? select)
    {
        if (_sections.GetValueOrDefault(RepoSection.Saves) is not MenuItemViewModel saves)
        {
            return false;
        }

        if (ReferenceEquals(NavManager.Current, saves))
        {
            // Already open, so selecting it again constructs nothing. Tell the page the user is
            // looking at instead.
            if (NavManager.CurrentPage is RepoSavegamesPageViewModel page && select is Guid savegameId)
            {
                page.Select(savegameId);
            }

            return true;
        }

        _selectSavegameOnce = select;

        var selected = TrySelect(saves);

        // Refused, so nothing read the value and it must not be waiting for whoever opens Saves next.
        _selectSavegameOnce = null;

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
    private async Task<bool> TrySelectSavegameAsync(Guid savegameId)
    {
        var archived = false;

        try
        {
            await _savegames.RefreshArchivedAsync(_repo.Id, CancellationToken.None);

            archived = _savegames.Archived(_repo.Id).Any(x => x.Id == savegameId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Falling through to the live list is the better guess of the two.
            _logger.LogInformation(exception, "Could not read the archived savegames of repo {RepoId} to find savegame {SavegameId}.", _repo.Id, savegameId);
        }

        return archived
            ? TrySelectArchive(savegameId)
            : TrySelectSavegames(savegameId);
    }

    /// <summary>
    /// Selects a profile and hands back the page it opened. Falls through to the archive for one that
    /// has been put away - see <see cref="OpenArchivedProfileAsync"/>.
    /// </summary>
    /// <returns>
    /// Null where the profile is gone entirely, or where the page in front of the user refused to be
    /// navigated away from.
    /// </returns>
    private async Task<ProfilePageViewModel?> TrySelectProfileAsync(Guid profileId)
    {
        // A repo opened a moment ago has its profile list still on the way, so a deep link arriving
        // first has to wait for it rather than concluding the profile does not exist.
        if (FindProfile(profileId) is null)
        {
            await LoadProfilesCommand.ExecuteAsync(null);
        }

        var entry = FindProfile(profileId) ?? await OpenArchivedProfileAsync(profileId);

        if (entry is null || TrySelect(entry) is false)
        {
            return null;
        }

        return NavManager.CurrentPage as ProfilePageViewModel;
    }

    /// <param name="highlight">The row to pick out on arrival, or null for none.</param>
    /// <returns>False where navigation was refused.</returns>
    private bool TrySelectArchive(Guid? highlight)
    {
        var archive = _sections[RepoSection.Archive];

        if (ReferenceEquals(NavManager.Current, archive))
        {
            return true;
        }

        _highlightInArchiveOnce = highlight;

        var selected = TrySelect(archive);

        // Refused, so nothing read the value and it must not be waiting for whoever opens the
        // Archive next.
        _highlightInArchiveOnce = null;

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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A link that cannot be followed does nothing. Nothing here is worth interrupting the
            // user for.
            _logger.LogInformation(exception, "Could not read the archived profiles of repo {RepoId} to open profile {ProfileId}.", _repo.Id, profileId);

            return null;
        }

        if (archived is null)
        {
            return null;
        }

        ClearArchivedProfile();

        var entry = new ProfileItemViewModel(_repo, new Profile(archived), _profilePageViewModelFactory);
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

        foreach (var entry in ArchivedProfiles)
        {
            entry.Dispose();
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

            // The transient archived entry exists for one visit. Any other page opening - including
            // the Archive itself, and including none at all - is the end of it. Asked once the page
            // has changed rather than whenever Selected does, because Selected passes through null
            // on every navigation, including one that is refused.
            if (NavManager.Current is not MenuItemViewModel current || ArchivedProfiles.Contains(current) is false)
            {
                ClearArchivedProfile();
            }

            return;
        }

        if (e.PropertyName != nameof(NavigationManager.Selected))
        {
            return;
        }

        OnPropertyChanged(nameof(ShowConnectGame));
        OnPropertyChanged(nameof(IsCreateProfileSelected));
    }

    private void OnProfileCreated(Profile created)
    {
        if (created.RepoId == _repo.Id && FindProfile(created.Id) is ProfileItemViewModel profile)
        {
            NavManager.Selected = profile;
        }
    }

    private MenuItemViewModel OverviewItem => _sections[RepoSection.Overview];

    private ProfileItemViewModel? FindProfile(Guid profileId)
        => Profiles.OfType<ProfileItemViewModel>().FirstOrDefault(x => x.Id == profileId);

    private ProfileItemViewModel MapProfileToVm(Profile profile)
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

        UpdateSavesBadge();
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


    private void OnBindingsChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.InvokeAsync(UpdateSavesBadge);

    /// <summary>Marks Saves while a save from this repo is checked out into a slot of the connected game.</summary>
    private void UpdateSavesBadge()
    {
        var held = ConnectedGame() is Game game
            && _bindings.GetBindings(game.Identity).Any(x => x.RepoId == _repo.Id);

        _sections.GetValueOrDefault(RepoSection.Saves)?.Badge = held ? "Checked out" : null;
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoPageViewModel Create(Repo repo)
        {
            return ActivatorUtilities.CreateInstance<RepoPageViewModel>(serviceProvider, repo);
        }
    }
}
