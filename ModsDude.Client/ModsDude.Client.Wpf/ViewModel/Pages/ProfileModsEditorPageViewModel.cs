using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.ModVersions;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The profile's mod list: everything available on the left, everything the profile pins on the
/// right.
/// </summary>
/// <remarks>
/// <para>
/// The left list is the <em>union</em> of what the repo has registered and what the enabled sources
/// hold, so a mod can be added to a profile and imported in one action rather than requiring a
/// detour to the management page first. It carries the same source list as that page, because adding
/// a mod straight out of Downloads while building a profile is the point of having sources at all.
/// </para>
/// <para>
/// <b>Updates render on the right.</b> A mod already in the profile never appears on the left, so an
/// available newer version shows as an affordance on the row that already exists rather than putting
/// the same mod on both sides at once.
/// </para>
/// <para>
/// <b>Nothing is uploaded until Save.</b> A local-only mod moved rightwards is a pending row; Save
/// imports the files and then writes the dependencies. Importing on the way in would make Cancel
/// meaningless and litter the repo with mods nobody kept. A save whose import does not fully succeed
/// writes nothing at all - see <see cref="SaveChanges"/> for why that has to be decided there.
/// See docs/09-mod-catalog.md#profile-mod-list-editor.
/// </para>
/// <para>
/// <b>Save re-applies by default.</b> The user came here to fold what the game did into the profile;
/// the re-apply is what actually reverts an auto-updated locked map, and separating it into a second
/// deliberate action is precisely how it gets forgotten. The targets are derived rather than asked -
/// see <see cref="ProfileApplyTarget"/> - and <em>Save only</em> costs a second click through the
/// dropdown, because a control that can be left in the dangerous position turns a per-save decision
/// into a standing mode.
/// </para>
/// </remarks>
public partial class ProfileModsEditorPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ProfileDto _profile;
    private readonly ModCatalog _catalog;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly ProfileSaveService _saveService;
    private readonly IModDependenciesClient _dependenciesClient;
    private readonly IProfilesClient _profilesClient;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly IDialogService _dialogService;
    private readonly NavigationLockService _navigationLock;
    private readonly GameRepository _gameRepository;
    private readonly ProfileApplyService _applyService;
    private readonly ModSyncService _syncService;
    private readonly DriftMonitor _driftMonitor;
    private readonly NoticeCenterViewModel _notices;
    private readonly IResourceLeases _leases;
    private readonly IToastService _toasts;
    private readonly ActiveProfile _activeProfile;

    /// <summary>
    /// The toast offering to undo the last bulk move, while it is up. Held so the next change of any
    /// kind can take it down: it is an undo only for as long as nothing has been built on top of it.
    /// </summary>
    private IToast? _undoToast;

    /// <summary>
    /// The toast asking whether to put this profile on a game, after a save that had nothing to apply
    /// to. Held so leaving the page takes the offer with it.
    /// </summary>
    private IToast? _activationToast;

    /// <summary>
    /// The versions a save this page rejoined is importing.
    /// </summary>
    /// <remarks>
    /// Merged into <see cref="_versionsByMod"/> beside what the catalog turns up, because they
    /// <em>are</em> the draft's own - and a page rebuilt while a save runs has its sources switched
    /// off, so nothing else here has any record of the files that save is uploading. Without them
    /// every pending row in the adopted draft would resolve to the unknown-version placeholder, which
    /// reports <c>IsOnServer: true</c> and would have the list claim the repo already holds what is
    /// still going up. Held until the save commits rather than folded into anything permanent: once
    /// it has, the repo holds these and the reload reads them from the registered half, and once it
    /// has not, the pending rows still need them.
    /// </remarks>
    private IReadOnlyList<CatalogModVersion> _adopted = [];

    /// <summary>
    /// Whether the repo's own registered versions are one of the sources the left list is composed
    /// from. On, like a source that is always available, and switched off from the same chip row the
    /// folders use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A filter over the catalog, never a change to it.</b> The pinned rows and every row's version
    /// selector are built from the same snapshot, and a profile pins registered versions - so dropping
    /// them from the catalog would turn every pinned row into an unresolvable placeholder. It is the
    /// left list's own composition that leaves them out, which is why unticking this is instant and
    /// costs no round trip.
    /// </para>
    /// <para>
    /// <b>What it is for</b> is the question the left list could not answer: with the repo off, what
    /// is left is exactly what the other enabled sources hold - the folders on this computer, or
    /// another profile's list - which is the set somebody adding new things to a profile is looking
    /// for, and which was previously buried among a few thousand rows the repo already had.
    /// </para>
    /// </remarks>
    private bool _includeRegistered = true;

    /// <summary>
    /// The other profiles in this repo that are being read as sources, in the order they were added.
    /// </summary>
    /// <remarks>
    /// View-scoped like an ad-hoc folder, and composed here rather than by
    /// <see cref="ModCatalog"/> - a profile's pins are registered versions by foreign key, so the
    /// catalog already holds every one of them and there is nothing to scan.
    /// </remarks>
    private readonly List<ProfileModSource> _profileSources = [];

    /// <summary>
    /// The places outside this machine the repo's game knows of - ModHub - each as a chip that starts
    /// switched on. Empty for a game with none.
    /// </summary>
    /// <remarks>
    /// Composed here like the profiles, and for a stronger reason: what they contribute is not versions
    /// at all but a link on a mod's row, so nothing about them reaches the catalog, the version index or
    /// the left list's membership. See <see cref="ModSourceKind.Remote"/>.
    /// </remarks>
    private readonly List<RemoteModSourceState> _remoteSources;

    /// <summary>
    /// The newer version each mod could be fetched at, from whichever enabled remote source answered
    /// first, as the chip its rows wear. Recomputed from <see cref="_versionsByMod"/> whenever that is,
    /// because an offer stops being one the moment the version it names is known here.
    /// </summary>
    private Dictionary<ModKey, RemoteOfferViewModel> _remoteOffers = [];

    /// <summary>
    /// Every version the enabled sources offer between them: what the repo has registered while its
    /// chip is on, whatever the enabled folders hold, and whatever the enabled profiles pin. This is
    /// what the left list is composed from, one row per mod.
    /// </summary>
    private HashSet<ModVersionIdentity> _offered = [];

    /// <summary>
    /// The same, by mod. What <see cref="PinnedModFilter.NotInSources"/> reads, which is a question
    /// about the mod rather than the version: a mod the other profile holds at a different version is
    /// an update, not a removal, and the left list already says so.
    /// </summary>
    private HashSet<ModKey> _offeredMods = [];

    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>
    /// Set by <em>Save only</em> for exactly one save and cleared as that save reads it. A control the
    /// user could leave switched on would convert a per-save decision into a standing mode, which is
    /// the opposite of what it is for.
    /// </summary>
    private bool _skipApplyOnce;

    /// <summary>
    /// A recompose that arrived while a save was running, held until it is over.
    /// </summary>
    /// <remarks>
    /// A save writes what was on screen when Save was pressed, and the rows the import reports into
    /// are the ones that were there then - so rebuilding the lists under it would both replace those
    /// rows and change what the page would be read back as. The one caller that can do this without
    /// the user touching anything is a drift notice clicked while the save runs.
    /// </remarks>
    private bool _recomposeWhenSaved;

    /// <summary>
    /// Every known version of every known mod, ordered per mod, with the pairs nothing settled kept
    /// beside them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from <see cref="ModCatalogSnapshot.Known"/> - the enabled sources <em>and</em> the ones
    /// on standby - union'd with what the draft is pinning. A pending row whose source has just been
    /// switched off keeps its pin, keeps the occurrence that names the file on disk, stays reported
    /// as pending and still imports on save, because a source switched off is still being read.
    /// Disabling a source is a statement about what is <em>looked at</em>, never about what exists.
    /// </para>
    /// <para>
    /// <b>It is not an accumulation.</b> This used to be a dictionary on the page that only ever grew,
    /// which held versions against a chip being unticked and equally held them against a rescan
    /// finding the file gone - so a deleted archive stayed in every selector and stayed counted as an
    /// available update, and pressing <em>Update all</em> moved a pin onto a file that was not there.
    /// Standby sources put that distinction where it can be made: in the catalog, which knows what it
    /// re-read and what it merely stopped merging.
    /// </para>
    /// </remarks>
    private IReadOnlyDictionary<ModKey, ModVersionSet> _versionsByMod =
        new Dictionary<ModKey, ModVersionSet>();

    /// <summary>What the profile held when it was last read from the server. Save diffs against it.</summary>
    private IReadOnlyList<ProfileModPin> _original = [];

    /// <summary>
    /// The revision <see cref="_original"/> was read at, and what a save is based on. Taken from the
    /// same response the list came out of rather than from the profile, because that is the only
    /// form of it that cannot already be stale by the time it is used.
    /// </summary>
    private int _basedOn;

    private IReadOnlyList<ProfileModRowViewModel> _available = [];
    private ProfileModUpdatePlan _updates = ProfileModUpdatePlan.Empty;

    /// <summary>What mods the profile holds, kept as a set because it is asked once per row.</summary>
    private HashSet<ModKey> _pinnedIds = [];

    /// <summary>
    /// Which version of each of them, which is what the left list actually hides.
    /// </summary>
    /// <remarks>
    /// <b>The hide rule is about versions, not mods.</b> A new version of a pinned mod is not in this
    /// profile, whatever else is - so it belongs on the left, where its row's verb is a version
    /// change rather than an add.
    /// </remarks>
    private Dictionary<ModKey, ModVersionKey> _pinnedVersions = [];

    /// <summary>
    /// The same pins with whether each is held in place, which is all the ignore rule reads of them.
    /// Its own dictionary because <see cref="_pinnedVersions"/> is asked a different question per row.
    /// </summary>
    private Dictionary<ModKey, (ModVersionKey Version, bool Locked)> _pinnedState = [];

    /// <summary>
    /// The mods this draft ignores. Edited by the eye buttons and written by a save, like the pins - see
    /// <see cref="ProfileIgnoring"/> for how a pin overrides it, and <see cref="DesiredIgnored"/> for what is
    /// actually written.
    /// </summary>
    private HashSet<ModKey> _ignoredMods = [];

    /// <summary>What the server held when it was last read. A save writes the ignore list only if it differs.</summary>
    private HashSet<ModKey> _originalIgnored = [];

    /// <summary>
    /// The mods this draft has moved to a version before the one the profile held when it was read. The
    /// user chose that, so the left list does not offer the way back as though it were an update - see
    /// <see cref="FindDowngraded"/>.
    /// </summary>
    private HashSet<ModKey> _downgraded = [];

    /// <summary>
    /// Mods the profile still holds on the server and this draft does not - taken out, and waiting
    /// for a save to write that. They are back on the left, which is where they would be if they had
    /// never been in the profile at all, so the sort is what tells the two apart. Keyed to the pin
    /// itself rather than just the id, because the left row's own + has to re-add at the version that
    /// was there rather than default to the newest - see <see cref="ShowRemovals"/> and
    /// <see cref="Recount"/>.
    /// </summary>
    private Dictionary<ModKey, ProfileModPin> _pendingRemovals = [];

    /// <summary>
    /// When each mod entered the profile at the version the server holds, as the server read it. What a
    /// pinned row still at that version shows and sorts by - see <see cref="AddedFor"/>.
    /// </summary>
    private Dictionary<ModKey, (ModVersionKey Version, DateTime Added)> _originalAdded = [];

    /// <summary>
    /// When this draft first put each mod at the version it now has, for a mod whose pin is not what the
    /// server holds. Pruned to what is still pinned on every recount, so taking a mod out and putting it
    /// back is a fresh event and not a resumed one.
    /// </summary>
    private Dictionary<ModKey, (ModVersionKey Version, DateTime At)> _draftedAt = [];

    /// <summary>
    /// Tracked rather than re-derived from the repo, so a game dropped from its list is still
    /// unsubscribed from.
    /// </summary>
    private readonly List<Game> _watchedGames = [];

    /// <summary>
    /// Set while the list is being rebuilt wholesale - from the server, or by a bulk move. Every add
    /// into <see cref="Pinned"/> would otherwise recount against a draft that is only half written,
    /// and every row a bulk move touches would recount the selection it is part of.
    /// </summary>
    private bool _publishing;

    /// <summary>
    /// Set for the length of one recount, so the row writes it makes cannot start another. See
    /// <see cref="Recount"/>.
    /// </summary>
    private bool _recounting;


    public ProfileModsEditorPageViewModel(
        Repo repo,
        ProfileDto profile,
        ModCatalog.Factory catalogFactory,
        ModListItemViewModel.Factory itemFactory,
        ProfileSaveService saveService,
        IModDependenciesClient dependenciesClient,
        IProfilesClient profilesClient,
        IModalService modalService,
        IErrorReporter errorReporter,
        IDialogService dialogService,
        NavigationLockService navigationLock,
        GameRepository gameRepository,
        ProfileApplyService applyService,
        ModSyncService syncService,
        DriftMonitor driftMonitor,
        NoticeCenterViewModel notices,
        IResourceLeases leases,
        IToastService toasts)
    {
        _leases = leases;
        _toasts = toasts;
        _repo = repo;
        _profile = profile;
        _itemFactory = itemFactory;
        _saveService = saveService;
        _dependenciesClient = dependenciesClient;
        _profilesClient = profilesClient;
        _modalService = modalService;
        _errorReporter = errorReporter;
        _dialogService = dialogService;
        _navigationLock = navigationLock;
        _gameRepository = gameRepository;
        _applyService = applyService;
        _syncService = syncService;
        _driftMonitor = driftMonitor;
        _notices = notices;
        _activeProfile = new ActiveProfile(repo.Id, profile.Id);

        // The page owns the catalog and disposes it, so the per-source scan cache lives exactly as
        // long as the checkboxes that recompose from it.
        _catalog = catalogFactory.Create(repo);

        _remoteSources = [.. (repo.Adapter.GetBaseCapabilityAdapterFactory<IRemoteModSourcesAdapter>()?.Invoke().Sources ?? [])
            .Select(x => new RemoteModSourceState(x))];

        // An apply changes what is in a mod folder, which is what a scan of it was a picture of. Held from
        // here to Dispose: the page can be open while an apply is started from the profile bar, the drift
        // notice or its own save, and every one of them ends in the same event.
        _syncService.ModFolderChanged += OnModFolderChanged;

        ProfileName = profile.Name;

        PinnedView = (ListCollectionView)CollectionViewSource.GetDefaultView(Pinned);
        PinnedView.CustomSort = Comparer<ProfileModRowViewModel>.Create(ComparePinned);

        // One box over both lists, as on the repo mods page. A mod is only ever on one side, so a
        // search that reached only the left one answered half the question somebody was asking -
        // and the half it answered was the side they were least likely to be looking for.
        PinnedView.Filter = x => x is ProfileModRowViewModel row && PassesPinned(row);

        Pinned.CollectionChanged += (_, _) => OnPinnedChanged();

        // Both lists get the same selection, because taking mods out of a profile has to be as
        // cheap as putting them in - the two are the same job seen from opposite sides, and a page
        // that made one of them a per-row click would just move the tedium rather than remove it.
        AvailableSelection = new ModListSelection(() => AvailableView, () => _available, AddRows, "Add", DescribeAdd);
        PinnedSelection = new ModListSelection(() => PinnedView, PinnedRows, RemoveRows, "Take out");

        AvailableSelection.Changed += OnSelectionChanged;
        PinnedSelection.Changed += OnSelectionChanged;

        _repo.Games.CollectionChanged += OnGamesChanged;
        RefreshApplyTargets();

        // The one place the app-level drift notice is suppressed: somebody already looking at the
        // drifted profile's mod list does not need to be told about it.
        _notices.SuppressFor(_activeProfile);

        // A save into this repo blocking this page's own is a fact about the world, so the button
        // re-asks whenever a lease moves rather than only when the draft does.
        _leases.Changed += OnLeasesChanged;
    }


    public string ProfileName { get; }

    public ObservableCollection<ModSourceViewModel> Sources { get; } = [];

    /// <summary>The profile's pinned mods, one per mod - the domain allows no more than that.</summary>
    public ObservableCollection<ProfileModRowViewModel> Pinned { get; } = [];

    public ListCollectionView PinnedView { get; }

    /// <summary>What is not in the profile yet, registered or merely on disk.</summary>
    [ObservableProperty]
    private ICollectionView? _availableView;

    /// <summary>
    /// What the user has picked on each side. Selection lives on the rows rather than in the list
    /// controls, which is what lets it survive the search - see <see cref="ModListSelection"/>.
    /// </summary>
    public ModListSelection AvailableSelection { get; }

    public ModListSelection PinnedSelection { get; }

    /// <summary>
    /// Narrows the left list to one kind of row, on top of whatever the search is doing. Every bulk
    /// action on this page is counted against what the list is <em>showing</em>, so a filter is
    /// simply another way of saying which mods a bulk action is about.
    /// </summary>
    [ObservableProperty]
    private AvailableModFilter _availableFilter = AvailableModFilter.All;

    /// <inheritdoc cref="AvailableFilter"/>
    [ObservableProperty]
    private PinnedModFilter _pinnedFilter = PinnedModFilter.All;

    /// <summary>
    /// What the right list is ordered by. Name by default, the one order that does not move under the
    /// pointer as the draft is edited - the date sorts do, since a mod added or updated in the draft is
    /// the most recent thing in it. Not remembered between visits: it is a way of looking, not a setting.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinnedSortDirectionText))]
    private ProfileModSort _pinnedSort = ProfileModSort.Name;

    /// <summary>
    /// Whether the sort runs in its natural direction - A to Z, or oldest first. Reset to the sort's own
    /// default whenever the sort changes, because "descending" means opposite things for a name and for
    /// a date and carrying it across would open every date sort oldest-first.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinnedSortDirectionText))]
    private bool _pinnedSortAscending = true;

    /// <summary>What the direction button says the list is doing, and so what pressing it changes.</summary>
    public string PinnedSortDirectionText => (PinnedSort, PinnedSortAscending) switch
    {
        (ProfileModSort.Name, true) => "A to Z. Click to reverse.",
        (ProfileModSort.Name, false) => "Z to A. Click to reverse.",
        (_, true) => "Oldest first. Click to reverse.",
        _ => "Newest first. Click to reverse."
    };

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableCountText))]
    private int _availableCount;

    /// <summary>Everything the profile does not hold, whatever the search is showing of it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableCountText))]
    private int _availableTotal;

    /// <summary>Of those, how many the profile has never held - what a bulk add would take.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAllShownNewCommand))]
    private int _newCount;

    /// <summary>How many of the left list's rows are there because this draft took them out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemovalText))]
    [NotifyPropertyChangedFor(nameof(HasRemovals))]
    [NotifyCanExecuteChangedFor(nameof(RestoreRemovedCommand))]
    private int _removalCount;

    /// <summary>
    /// Whether the left list is showing what this draft has taken out. Defaults to shown: a removal
    /// is unsaved work, it sorts to the top so seeing it costs one glance, and hiding unsaved work by
    /// default is how people lose it. The count above is the control - clicking it is what flips
    /// this, the same shape as the updates band's skipped-locked count opening its own list.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemovalToggleTooltip))]
    private bool _showRemovals = true;

    /// <summary>
    /// Whether the left list is showing what is set apart as ignored. Off by default, which is the point
    /// of ignoring anything. <b>A filter, not a source:</b> it narrows what the enabled sources offer and
    /// never adds to it, so it composes with the search and the chips like any of them and sits with the
    /// list's count rather than in the source chips.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoredToggleTooltip))]
    private bool _showIgnored;

    /// <summary>
    /// How many rows the left list is leaving out, or showing dimmed, because they are ignored -
    /// counted against everything the list applies but this, so it says how many the toggle would
    /// reveal.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoredText))]
    [NotifyPropertyChangedFor(nameof(HasIgnored))]
    [NotifyPropertyChangedFor(nameof(IgnoredToggleTooltip))]
    private int _ignoredCount;

    /// <summary>Of those, how many the ordering could not compare against what the repo holds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmbiguousText))]
    [NotifyPropertyChangedFor(nameof(HasAmbiguousVersions))]
    private int _ambiguousCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinnedCountText))]
    [NotifyPropertyChangedFor(nameof(HasPinnedMods))]
    private int _pinnedCount;

    /// <summary>How many of those the search is showing. Equal to PinnedCount when nothing is typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinnedCountText))]
    [NotifyPropertyChangedFor(nameof(HasVisiblePinnedMods))]
    [NotifyCanExecuteChangedFor(nameof(RemoveAllShownCommand))]
    private int _pinnedVisibleCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingText))]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    [NotifyPropertyChangedFor(nameof(CanImportHere))]
    [NotifyPropertyChangedFor(nameof(SaveBlockedReason))]
    [NotifyPropertyChangedFor(nameof(HasSaveBlockedReason))]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveOnlyCommand))]
    private int _pendingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateCountText))]
    [NotifyPropertyChangedFor(nameof(RemoteUpdatesText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllUpdatesCommand))]
    private int _updateCount;

    /// <summary>
    /// How many pinned mods a remote source - ModHub - has a newer version of, leaving out the locked
    /// ones. Counted apart from <see cref="UpdateCount"/> rather than into it, because <em>Update all</em>
    /// cannot take them: there is no file here to move a pin to until somebody downloads one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateCountText))]
    [NotifyPropertyChangedFor(nameof(RemoteUpdatesText))]
    [NotifyPropertyChangedFor(nameof(HasRemoteUpdates))]
    private int _remoteUpdateCount;

    /// <summary>
    /// The locked ones, apart - the same split the band makes for updates here, and for the same reason:
    /// a lock is a decision not to move, and a count that included them would be asking anyway.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateCountText))]
    [NotifyPropertyChangedFor(nameof(RemoteUpdatesText))]
    [NotifyPropertyChangedFor(nameof(HasRemoteUpdates))]
    private int _remoteLockedUpdateCount;

    /// <summary>
    /// How many of those a save would have to import first, which is the other half of the band's
    /// sentence. Counted apart because the two cost differently: one is a pin moving and the other is
    /// a file going up.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateCountText))]
    private int _pendingUpdateCount;

    /// <summary>What <em>Update all</em> would move without uploading anything.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateRepoOnlyText))]
    [NotifyPropertyChangedFor(nameof(HasRepoOnlyUpdates))]
    [NotifyCanExecuteChangedFor(nameof(ApplyRepoUpdatesCommand))]
    private int _freeUpdateCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkippedText))]
    [NotifyPropertyChangedFor(nameof(HasSkippedUpdates))]
    [NotifyPropertyChangedFor(nameof(ApplyUpdatesText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllUpdatesCommand))]
    private int _skippedUpdateCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplyUpdatesText))]
    [NotifyPropertyChangedFor(nameof(HasRepoOnlyUpdates))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllUpdatesCommand))]
    private int _applicableUpdateCount;

    /// <summary>
    /// Whether any source at all is being read. The left list is composed from them, so with none
    /// enabled it is empty by construction - and the chip row above it is the one control that
    /// explains that, which is why nothing hides it.
    /// </summary>
    [ObservableProperty]
    private bool _hasEnabledSources = true;

    /// <summary>
    /// Whether any <em>folder</em> is being read, which is a different question and the one the
    /// updates band has to answer at zero: an on-disk update only exists while its folder's chip is
    /// on, so "no updates" with nothing being scanned would be claiming to have looked.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateCountText))]
    private bool _hasEnabledFolders;

    /// <summary>
    /// Whether a folder has been read at all this session, once true forever - see
    /// <see cref="RebuildSources"/>, the only place it is set. Plain rather than observable: it only
    /// ever needs to poke <see cref="UpdateCountText"/> on the one transition that matters.
    /// </summary>
    private bool _hasReadAnyFolder;

    /// <summary>
    /// Whether a left-hand row names the sources its version was found in. With a single source
    /// enabled every row would name the same one, which is just noise. Held as a field rather than
    /// passed down, because the left list is rebuilt from <see cref="Recount"/> as well as from a
    /// composition and only one of the two has a snapshot in its hand.
    /// </summary>
    private bool _showSources;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveOnlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// Whether the mod list itself differs from the server's: the part of a save that is a revision, an
    /// import and a re-apply. A draft with only <see cref="HasIgnoredChanges"/> is saved without any of
    /// them, and its button says so.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveActionText))]
    [NotifyPropertyChangedFor(nameof(WillApply))]
    [NotifyCanExecuteChangedFor(nameof(SaveOnlyCommand))]
    private bool _hasModListChanges;

    /// <summary>Whether the ignore list differs from the server's.</summary>
    [ObservableProperty]
    private bool _hasIgnoredChanges;

    /// <summary>
    /// Whether the page is showing what the draft would change instead of the two lists. Where a save
    /// is watched as well as where it is checked: the import is reported on these rows, not on the
    /// lists', and the lists are only hidden rather than dropped so the search, the selection and the
    /// scroll position are all still there on the way back.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewButtonText))]
    private bool _isReviewing;

    /// <summary>How many mods the draft would add, change or take out. What the review is a list of.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewButtonText))]
    private int _changeCount;

    /// <summary>The count broken down, in the order the review's groups run.</summary>
    [ObservableProperty]
    private string _changeSummary = string.Empty;

    /// <summary>The review's rows, grouped. Null while the review is not being shown.</summary>
    [ObservableProperty]
    private ICollectionView? _changesView;

    public string ReviewButtonText => IsReviewing ? "Back to editing" : $"Review changes ({ChangeCount})";

    /// <summary>What the review is showing, so the run's marks can be put on it as it is built and as they arrive.</summary>
    private IReadOnlyList<DraftChangeViewModel> _changeRows = [];

    /// <summary>
    /// The save whose import the review is showing. Held apart from the rows because the rows are
    /// rebuilt from the draft - after a failed save, after a revert - and a rebuilt row has to be told
    /// again how its import went, or the review of a failed save would show nothing failing.
    /// </summary>
    private ProfileSaveRun? _markedRun;

    /// <summary>
    /// What to call this save in the profile's history. Optional, and never required: a field the
    /// save button refused to work without would be answered with "asdf" by the third save, and a
    /// history of "asdf" is worse than a history of unnamed revisions with honest counts.
    /// </summary>
    /// <remarks>
    /// Borrowed wording. Fusion 360 calls the same field on the same gesture a <em>version
    /// description</em>, and somebody who has used a CAD package will recognise it - which is worth
    /// more than internal consistency with the word "revision" everywhere else on the page. It maps
    /// to <c>ProfileRevision.Label</c>, which stays neutrally named because the domain already
    /// spends the word "version" on a mod's.
    /// </remarks>
    [ObservableProperty]
    private string _versionDescription = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveOnlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopSavingCommand))]
    private bool _isSaving;

    /// <summary>
    /// Whether nothing on this page may change the draft, because this profile is being saved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Per control rather than per list.</b> <c>IsEnabled</c> on a <see cref="System.Windows.Controls.ListBox"/>
    /// stops the mouse wheel along with everything else, so this binds to what can actually change
    /// the draft: the row buttons, the selection checkboxes, the version selectors, the lock toggles,
    /// drag-and-drop and the source chips. The lists, their scrolling and the mod name that opens the
    /// details dialog stay live - reading is not writing - and so do the search and the filter chips,
    /// which change the view and nothing else.
    /// </para>
    /// <para>
    /// <b>Not the same as <see cref="IsSaving"/>.</b> The save belongs to the profile rather than to
    /// this page, so an editor built for a profile a save is already running on comes up read-only
    /// without having started anything.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyRepoUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddAllShownNewCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreRemovedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveAllShownCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(IgnoreSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnignoreSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleIgnoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(LockSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnlockSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFromProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteListCommand))]
    [NotifyCanExecuteChangedFor(nameof(RescanAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddProfileSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveOnlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    private bool _isReadOnly;

    /// <summary>
    /// The game this save re-applies to, or null where none follows this profile.
    /// </summary>
    /// <remarks>
    /// <b>One or none, never a list.</b> A profile belongs to a repo, a repo is about one game, and a
    /// machine configures that game once - so the read-only disclosure of "these are the games this
    /// applies to" had nothing left to disclose, and the word "game" never appears on this page at
    /// all now. The <em>folders</em> it reaches may well be several, which is the apply's business
    /// and not this button's.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveActionText))]
    [NotifyPropertyChangedFor(nameof(HasApplyTargets))]
    [NotifyPropertyChangedFor(nameof(WillApply))]
    [NotifyCanExecuteChangedFor(nameof(SaveOnlyCommand))]
    private Game? _applyTarget;

    public bool HasApplyTargets => ApplyTarget is not null;

    /// <summary>
    /// Whether a save would re-apply the profile. Not for one that only changes what is ignored: that
    /// changes nothing a folder was built from, so applying it would be a pointless rewrite of installed
    /// mods - and its button must not say otherwise.
    /// </summary>
    public bool WillApply => HasApplyTargets && HasModListChanges;

    public string SaveActionText => ProfileApplyTarget.DescribeSaveAction(WillApply);

    /// <summary>
    /// Worded with the consequence rather than as a caution. Someone reading only the label has to be
    /// able to tell what it leaves behind.
    /// </summary>
    public string SaveOnlyDescription =>
        "Saves the profile but leaves your installed mods untouched. Your locked mods stay at the versions " +
        "the game updated them to. Only if you know exactly what you are doing.";

    /// <summary>
    /// What every control that can change the draft binds its <c>IsEnabled</c> to. The inverse of
    /// <see cref="IsReadOnly"/>, said the way a view has to say it.
    /// </summary>
    public bool CanEdit => IsReadOnly is false;

    public bool HasPinnedMods => PinnedCount > 0;

    /// <summary>
    /// Whether the right list is showing anything. Distinct from <see cref="HasPinnedMods"/> now the
    /// search reaches this side: a profile with two thousand mods and no match for what was typed is
    /// an empty box that has to say why, and "nothing in this profile yet" would be a lie.
    /// </summary>
    public bool HasVisiblePinnedMods => PinnedVisibleCount > 0;
    public bool HasRemovals => RemovalCount > 0;
    public bool HasPending => PendingCount > 0;
    public bool HasSkippedUpdates => SkippedUpdateCount > 0;

    public string AvailableCountText => Describe(AvailableCount, AvailableTotal);

    /// <summary>
    /// Says why the top of the left list is not alphabetical. Worded as what a save will do, because
    /// until then the profile still holds them. Doubles as the control that hides them - see
    /// <see cref="ShowRemovals"/> - so it says which way clicking it goes.
    /// </summary>
    public string RemovalText => RemovalCount == 1
        ? "1 taken out"
        : $"{RemovalCount} taken out";

    public string RemovalToggleTooltip => ShowRemovals
        ? "Taken out of the profile, and shown here until you save. Click to hide them."
        : "Taken out of the profile and hidden. Click to show them again.";

    public bool HasIgnored => IgnoredCount > 0;

    public string IgnoredText => IgnoredCount == 1 ? "1 ignored" : $"{IgnoredCount} ignored";

    /// <summary>
    /// Says which way clicking goes, and what "ignored" covers - the second half is the part that is
    /// not obvious from a count, since some of them nobody chose.
    /// </summary>
    public string IgnoredToggleTooltip => ShowIgnored
        ? "Showing ignored mods, dimmed. Click to hide them again."
        : IgnoredCount == 0
            ? "Nothing is ignored here. Mods you ignore, and other versions of mods this profile locks, are hidden."
            : "Hidden: mods you have ignored in this profile, and other versions of mods it locks. Click to show them.";

    public string AmbiguousText => AmbiguousCount == 1
        ? "1 version could not be compared"
        : $"{AmbiguousCount} versions could not be compared";

    public bool HasAmbiguousVersions => AmbiguousCount > 0;

    public string PinnedCountText => Describe(PinnedVisibleCount, PinnedCount);

    /// <summary>
    /// The same wording as the repo mods page, and for the same reason: with one box filtering both
    /// lists, a count that only ever said "412 mods" could not say whether the search had found
    /// nothing or the list was empty.
    /// </summary>
    private static string Describe(int visible, int total)
        => visible == total ? total == 1 ? "1 mod" : $"{total} mods" : $"{visible} of {total} mods";

    public string PendingText => PendingCount == 1
        ? "1 mod will be imported when you save"
        : $"{PendingCount} mods will be imported when you save";

    /// <summary>
    /// Both kinds of update, and the split between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reads at zero as well as above it.</b> The section it heads is always on screen, so "none"
    /// is an answer it has to be able to give - and it is the answer someone who came here to check
    /// for updates was looking for.
    /// </para>
    /// <para>
    /// <b>And is honest there.</b> An on-disk update only exists while its folder's chip is on, so
    /// with no folder being read "no updates available" would be claiming to have looked. It says
    /// what it actually checked instead.
    /// </para>
    /// </remarks>
    public string UpdateCountText
    {
        get
        {
            if (UpdateCount == 0)
            {
                // "No updates available" beside "5 on ModHub" would contradict itself, so with remote
                // ones beside it this says only what it checked here.
                if (HasRemoteUpdates)
                {
                    return _hasReadAnyFolder ? "No updates here" : "No updates in this repo";
                }

                // Whether any folder has ever been read this session, not just right now - a folder
                // switched back off after finding nothing was still looked at, and saying otherwise
                // would claim less than the page actually knows.
                return _hasReadAnyFolder
                    ? "No updates available"
                    : "No updates in this repo. No folders are being read.";
            }

            var text = UpdateCount == 1 ? "1 update available" : $"{UpdateCount} updates available";

            return PendingUpdateCount > 0
                ? $"{text} · {PendingUpdateCount} will be imported when you save"
                : text;
        }
    }

    /// <summary>
    /// The band's link to the updates a remote source has, beside the ones here: "5 more on ModHub · 2
    /// locked". A link to the Updates filter, like the ambiguous count beside it, because each one is a
    /// page to open rather than something a batch action can do.
    /// </summary>
    public string RemoteUpdatesText
    {
        get
        {
            var name = _remoteSources.Count == 1 ? _remoteSources[0].Remote.DisplayName : "online";

            var text = RemoteUpdateCount == 0
                ? $"{RemoteLockedUpdateCount} locked on {name}"
                : UpdateCount > 0 ? $"{RemoteUpdateCount} more on {name}" : $"{RemoteUpdateCount} on {name}";

            return RemoteUpdateCount > 0 && RemoteLockedUpdateCount > 0
                ? $"{text} · {RemoteLockedUpdateCount} locked"
                : text;
        }
    }

    public bool HasRemoteUpdates => RemoteUpdateCount + RemoteLockedUpdateCount > 0;

    public string ApplyUpdatesText => ApplicableUpdateCount switch
    {
        0 => "Update all",
        1 => "Update 1 mod",
        _ => $"Update {ApplicableUpdateCount} mods"
    };

    /// <inheritdoc cref="ApplyRepoUpdates"/>
    public string UpdateRepoOnlyText => FreeUpdateCount == 1
        ? "Update the 1 already in the repo"
        : $"Update the {FreeUpdateCount} already in the repo";

    public string UpdateRepoOnlyDescription =>
        "Moves only the pins whose newer version this repo already holds, so nothing is uploaded.";

    /// <summary>
    /// Whether the caret has anything to offer that the button beside it does not. Its own condition
    /// rather than the primary's, unlike the save split: there can be a free half of a mixed set and
    /// there can equally be none, and a caret over a menu that says the same thing as the button is
    /// an invitation to nothing.
    /// </summary>
    public bool HasRepoOnlyUpdates => FreeUpdateCount > 0 && FreeUpdateCount < ApplicableUpdateCount;

    /// <summary>
    /// A link rather than a footnote: it opens the same dialog the per-row change opens, reached
    /// deliberately instead of fired at every save.
    /// </summary>
    public string SkippedText => SkippedUpdateCount == 1 ? "1 locked, skipped" : $"{SkippedUpdateCount} locked, skipped";


    #region Moving mods between the lists

    /// <summary>
    /// One row's verb, which depends on what the profile already has of that mod: a mod it does not
    /// hold is pinned, and a mod it holds at another version is moved to this one.
    /// </summary>
    /// <remarks>
    /// The move goes through the same confirmation the version selector does, because it is the same
    /// act - a locked pin has to be asked about before it moves, whichever control moved it.
    /// </remarks>
    [RelayCommand]
    private async Task Add(ProfileModRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var chosen = row.SelectedVersion.Version;

        if (FindPinned(chosen.ModId) is not ProfileModRowViewModel existing)
        {
            Pin(row, chosen);

            return;
        }

        if (existing.Versions.FirstOrDefault(x => x.Version.VersionId == chosen.VersionId)
            is not ProfileModVersionOption option
            || option.Version.VersionId == existing.SelectedVersion.Version.VersionId)
        {
            return;
        }

        if (existing.IsLocked && await ConfirmLockedVersionChangeAsync(existing, option) is false)
        {
            return;
        }

        existing.SetVersion(option.Version.VersionId);
    }

    /// <summary>
    /// Adds a row the caller has already established is not pinned, at whichever version its own
    /// selector is showing. Split out because the scan that establishes it is linear, and a bulk
    /// move that repeated it per row would be quadratic in a list that routinely runs to a couple of
    /// thousand.
    /// </summary>
    private void Pin(ProfileModRowViewModel row, CatalogModVersion chosen)
    {
        // Moving a mod across always drops it from the selection it was moved out of - including
        // when it was moved by its own button - so the left list is never left holding a picked row
        // that is no longer in it.
        row.IsSelected = false;

        Pinned.Add(CreatePinnedRow(VersionsFor(chosen), chosen, LockedByProfileSource(chosen.Identity)));
    }

    /// <summary>
    /// Every mod the left list is showing that this profile has never held, so the search and the
    /// filter together are how a subset is picked.
    /// </summary>
    /// <remarks>
    /// <b>Adding and upgrading are kept apart.</b> The rows that would move a pin this profile
    /// already has are excluded here and counted out of <see cref="NewCount"/> with them: a bulk add
    /// that silently moved pins would be a different act under the same label. Putting a removal back
    /// is <see cref="RestoreRemoved"/>, which is an undo with a version and a lock to restore rather
    /// than a default to pick.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanAddAllShownNew))]
    private void AddAllShownNew()
    {
        RunBulk(() =>
        {
            // The left list holds one row per mod, so nothing here can collide and none of it needs
            // re-checking.
            var rows = _available.Where(IsShownAndNew).ToList();

            foreach (var row in rows)
            {
                Pin(row, row.SelectedVersion.Version);
            }

            return Describe("Added", rows.Count);
        });
    }

    /// <summary>
    /// What a bulk add would take: shown, never held by this profile, and not one of its own
    /// removals waiting to be written.
    /// </summary>
    private bool IsShownAndNew(ProfileModRowViewModel row)
        => Passes(row)
        && IsPendingRemoval(row) is false
        && _pinnedIds.Contains(row.ModId) is false;

    private bool CanAddAllShownNew() => NewCount > 0 && IsReadOnly is false;

    /// <summary>
    /// Takes out everything the right list is showing. The counterpart of <see cref="AddAllShownNew"/>
    /// and deliberately its equal: a page where adding forty mods is one click and taking forty out
    /// is forty has not solved the problem, it has picked a side.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveAllShown))]
    private void RemoveAllShown()
    {
        RunBulk(() => Describe("Took out", RemoveMany(Pinned.Where(PassesPinned))));
    }

    private bool CanRemoveAllShown() => PinnedVisibleCount > 0 && IsReadOnly is false;

    /// <summary>
    /// Puts back everything this draft has taken out, at the version and lock the profile still holds
    /// on the server - which is what makes it an undo rather than a re-add. Not limited to what the
    /// search is showing: it undoes the removals, and a removal the user cannot currently see is
    /// still one of them.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreRemoved))]
    private void RestoreRemoved()
    {
        RunBulk(() =>
        {
            var restored = _pendingRemovals.Values.ToList();

            foreach (var pin in restored)
            {
                Pinned.Add(CreatePinnedRow(pin));
            }

            return Describe("Put back", restored.Count);
        });
    }

    private bool CanRestoreRemoved() => RemovalCount > 0 && IsReadOnly is false;

    /// <summary>
    /// The count beside the removals is the control, not just a label - the same shape as the
    /// updates band's skipped-locked count opening its own list. Reading is not writing, so this
    /// needs no <c>CanEdit</c> guard: it changes what the left list shows, never the draft.
    /// </summary>
    [RelayCommand]
    private void ToggleRemovals() => ShowRemovals = ShowRemovals is false;

    /// <summary>
    /// The row's eye: ignores the mod, or stops ignoring it. Which one is a question about the row
    /// rather than a mode, so the same button undoes what it did - and does nothing for a row whose
    /// ignore is not somebody's to reverse.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private void ToggleIgnore(ProfileModRowViewModel? row)
    {
        if (row is null || row.CanToggleIgnore is false)
        {
            return;
        }

        SetIgnored([row], row.IgnoreState is not IgnoreState.Ignored);
    }

    [RelayCommand(CanExecute = nameof(CanIgnoreSelected))]
    private void IgnoreSelected() => SetIgnored(PickedRows(x => x.CanToggleIgnore && x.IgnoreState is IgnoreState.None), true);

    [RelayCommand(CanExecute = nameof(CanUnignoreSelected))]
    private void UnignoreSelected() => SetIgnored(PickedRows(x => x.IgnoreState is IgnoreState.Ignored), false);

    private bool CanIgnoreSelected() => NotReadOnly() && PickedRows(x => x.CanToggleIgnore && x.IgnoreState is IgnoreState.None).Count > 0;

    private bool CanUnignoreSelected() => NotReadOnly() && PickedRows(x => x.IgnoreState is IgnoreState.Ignored).Count > 0;

    /// <summary>
    /// What the bulk buttons say, counted the way the buttons count. The picked rows the ignore cannot
    /// take - pinned, or ignored already by a lock - are simply not part of the number, so a button
    /// never promises more than it will do.
    /// </summary>
    public string IgnoreSelectedText => DescribeIgnore("Ignore", PickedRows(x => x.CanToggleIgnore && x.IgnoreState is IgnoreState.None).Count);

    public string UnignoreSelectedText => DescribeIgnore("Stop ignoring", PickedRows(x => x.IgnoreState is IgnoreState.Ignored).Count);

    private static string DescribeIgnore(string verb, int count) => count > 0 ? $"{verb} {count}" : verb;

    private List<ProfileModRowViewModel> PickedRows(Func<ProfileModRowViewModel, bool> where)
        => [.. AvailableSelection.Picked().OfType<ProfileModRowViewModel>().Where(where)];

    /// <summary>
    /// Ignores or releases some rows' mods in the draft. Nothing is written: like a pin, it is saved
    /// with the rest of the list - see <see cref="ProfileSaveService"/> for the order.
    /// </summary>
    /// <remarks>
    /// The rows let go of their selection on the way, exactly as a row moved into the profile does, so
    /// the bar under the list is never left holding rows the list is no longer showing.
    /// </remarks>
    private void SetIgnored(IReadOnlyList<ProfileModRowViewModel> rows, bool ignored)
    {
        var modIds = rows.Select(x => x.ModId).Distinct().ToList();

        if (modIds.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            row.IsSelected = false;
        }

        if (ignored)
        {
            _ignoredMods.UnionWith(modIds);
        }
        else
        {
            _ignoredMods.ExceptWith(modIds);
        }

        RefreshIgnored();

        if (Describe(ignored ? "Ignored" : "Stopped ignoring", modIds.Count) is string text)
        {
            _toasts.Show(text);
        }
    }

    /// <summary>
    /// The ignore list as it would be saved: what is ignored, minus what the draft pins.
    /// </summary>
    /// <remarks>
    /// <b>A pinned mod cannot also be ignored, and the pin wins.</b> A pin does not remove the mod from
    /// <see cref="_ignoredMods"/> - so discarding the pin puts the ignore back, and pinning then taking
    /// out again is not an edit - it just is not written while the pin stands. The server refuses a list
    /// that overlaps what it pins, so this is also what keeps a save from being refused.
    /// </remarks>
    private HashSet<ModKey> DesiredIgnored() => ProfileIgnoring.WithoutPinned(_ignoredMods, _pinnedIds);

    /// <summary>
    /// What both lists do when the ignored set changes without any pin changing. Lighter than a
    /// recount, deliberately: ignoring moves no pin and plans no update, and a recount would retire the
    /// bulk undo of a move that has nothing to do with it.
    /// </summary>
    private void RefreshIgnored()
    {
        foreach (var row in _available)
        {
            row.IgnoreState = IgnoreStateOf(row);
        }

        RefreshViews();

        OnSelectionChanged();

        RefreshUnsaved();
    }


    [RelayCommand(CanExecute = nameof(CanAddSelected))]
    private void AddSelected() => AddRows(AvailableSelection.Picked());

    /// <summary>
    /// Not while every picked row is a locked pin: the button would do nothing, and the bar says why.
    /// A selection with anything else in it still moves that part and leaves the locked rows alone.
    /// </summary>
    private bool CanAddSelected()
    {
        if (NotReadOnly() is false)
        {
            return false;
        }

        var counts = CountMoves(AvailableSelection.Picked());

        return counts.Movable > 0 || counts.Locked == 0;
    }

    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private void RemoveSelected() => RemoveRows(PinnedSelection.Picked());

    /// <summary>
    /// Moves picked rows into the profile and lets go of them on the way, so the left list is not
    /// left holding a selection of rows that are no longer in it.
    /// </summary>
    /// <remarks>
    /// Acts on everything picked, including rows the search or the filter is currently hiding - which
    /// is the whole point of a selection that outlives the search, and why the bar above says how
    /// many of them are off screen and offers to put those down.
    /// </remarks>
    /// <remarks>
    /// <b>Two moves under one button.</b> Since the left list is about versions, a picked row can be
    /// a mod the profile has never held or a newer version of one it holds - so this adds the first
    /// kind and moves the pin for the second, and says which it did how many of. Locked pins are left
    /// alone and counted, exactly as the batch update leaves them: a lock is not a question a
    /// selection gets to answer.
    /// </remarks>
    private void AddRows(IReadOnlyList<ISelectableRow> rows)
    {
        RunBulk(() =>
        {
            // A picked row may have been moved by its own button in the meantime, so the draft is
            // read once into a lookup rather than re-scanned per row.
            var pinned = Pinned.ToDictionary(x => x.ModId);
            var counts = new MoveCounts();

            foreach (var row in rows.OfType<ProfileModRowViewModel>())
            {
                var kind = Classify(row, pinned);

                counts = counts.With(kind);

                if (kind is ProfileVersionMove.Add)
                {
                    var chosen = row.SelectedVersion.Version;

                    Pin(row, chosen);

                    pinned[chosen.ModId] = Pinned[^1];
                }
                else if (kind is ProfileVersionMove.Update or ProfileVersionMove.Move)
                {
                    pinned[row.ModId].SetVersion(row.SelectedVersion.Version.VersionId);
                }

                row.IsSelected = false;
            }

            return DescribeMoves(counts);
        });
    }

    /// <summary>How a selection sorts into <see cref="ProfileVersionMove"/>s, which is what its button says.</summary>
    private readonly record struct MoveCounts(int Adds, int Updates, int Moves, int Locked)
    {
        /// <summary>Everything the button would actually do. Locked rows are counted apart: they will not move.</summary>
        public int Movable => Adds + Updates + Moves;

        public MoveCounts With(ProfileVersionMove kind) => kind switch
        {
            ProfileVersionMove.Add => this with { Adds = Adds + 1 },
            ProfileVersionMove.Update => this with { Updates = Updates + 1 },
            ProfileVersionMove.Move => this with { Moves = Moves + 1 },
            ProfileVersionMove.Locked => this with { Locked = Locked + 1 },
            _ => this
        };
    }

    private ProfileVersionMove Classify(ProfileModRowViewModel row, IReadOnlyDictionary<ModKey, ProfileModRowViewModel> pinned)
    {
        var chosen = row.SelectedVersion.Version;
        var held = pinned.GetValueOrDefault(chosen.ModId);

        return ProfileVersionMoves.Classify(
            chosen.VersionId,
            held?.SelectedVersion.Version.VersionId,
            held?.IsLocked ?? false,
            _versionsByMod.GetValueOrDefault(chosen.ModId));
    }

    private MoveCounts CountMoves(IReadOnlyList<ISelectableRow> rows)
    {
        var pinned = Pinned.ToDictionary(x => x.ModId);
        var counts = new MoveCounts();

        foreach (var row in rows.OfType<ProfileModRowViewModel>())
        {
            counts = counts.With(Classify(row, pinned));
        }

        return counts;
    }

    /// <summary>What a mixed bulk move turned out to do, or null for one that did nothing.</summary>
    private static string? DescribeMoves(MoveCounts counts)
    {
        var moved = counts.Movable == 0
            ? null
            : Sentence(
                [("added", counts.Adds), ("updated", counts.Updates + counts.Moves)],
                counts.Movable);

        return (moved, counts.Locked) switch
        {
            (null, 0) => null,
            (null, var left) => $"Nothing moved - {Locked(left)}",
            (var text, 0) => text,
            (var text, var left) => $"{text}, {Locked(left)}"
        };
    }

    /// <summary>
    /// What the left list's selection bar says its button will do. Every verb the selection spans,
    /// because a selection that does several things and a label that names one of them is a label that
    /// lies about what pressing it does - and what the profile would refuse is said beside it, so a
    /// button that will not touch a locked mod says so before it is pressed rather than after.
    /// </summary>
    private string DescribeAdd(IReadOnlyList<ISelectableRow> picked)
    {
        var counts = CountMoves(picked);

        var text = counts.Movable == 0
            ? counts.Locked > 0 ? "Nothing to update" : "Add"
            : Sentence(
                [("add", counts.Adds), ("update", counts.Updates + counts.Moves)],
                counts.Movable);

        return counts.Locked > 0 ? $"{text} ({counts.Locked} locked)" : text;
    }

    /// <summary>
    /// The verbs that have something to do, in one phrase: <c>Add 3 mods</c> for one of them and
    /// <c>Add 3 and update 2</c> for several, its first word capitalised either way.
    /// </summary>
    private static string Sentence(IReadOnlyList<(string Verb, int Count)> verbs, int total)
    {
        var active = verbs.Where(x => x.Count > 0).ToList();

        if (active.Count == 1)
        {
            return $"{Capitalised(active[0].Verb)} {(total == 1 ? "1 mod" : $"{total} mods")}";
        }

        var parts = active.Select(x => $"{x.Verb} {x.Count}").ToList();

        return Capitalised($"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}");
    }

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private void RemoveRows(IReadOnlyList<ISelectableRow> rows)
    {
        RunBulk(() => Describe("Took out", RemoveMany(rows.OfType<ProfileModRowViewModel>())));
    }

    /// <summary>
    /// Takes out several rows in one backwards pass, and answers how many. Walking the draft once
    /// rather than searching it per row keeps a bulk removal linear, and taking the survivors as they
    /// stand - rather than rebuilding the list from its pins - is what keeps their loaded artwork and
    /// their own selection intact.
    /// </summary>
    private int RemoveMany(IEnumerable<ProfileModRowViewModel> rows)
    {
        var doomed = new HashSet<ProfileModRowViewModel>(rows);
        var removed = 0;

        for (var index = Pinned.Count - 1; index >= 0; index--)
        {
            var row = Pinned[index];

            if (doomed.Contains(row) is false)
            {
                continue;
            }

            row.PropertyChanged -= OnPinnedRowChanged;

            Pinned.RemoveAt(index);

            removed++;
        }

        return removed;
    }

    [RelayCommand]
    private void DeselectHiddenAvailable() => AvailableSelection.DeselectHidden();

    [RelayCommand]
    private void DeselectHiddenPinned() => PinnedSelection.DeselectHidden();

    [RelayCommand]
    private void ClearAvailableSelection() => AvailableSelection.ClearSelection();

    [RelayCommand]
    private void ClearPinnedSelection() => PinnedSelection.ClearSelection();

    [RelayCommand]
    private void SelectAllShownAvailable() => AvailableSelection.SelectAllShown();

    [RelayCommand]
    private void SelectAllShownPinned() => PinnedSelection.SelectAllShown();

    /// <summary>
    /// Runs a bulk change to the profile and recounts once. At a couple of thousand mods, recounting
    /// per insert would re-plan every update and re-sort both lists two thousand times over.
    /// </summary>
    private void InBulk(Action change)
    {
        _publishing = true;

        try
        {
            change();
        }
        finally
        {
            _publishing = false;
        }

        Recount();
    }

    /// <summary>
    /// A bulk change that can be taken back, described by whatever it turned out to do. The change
    /// returns its own wording because the count is only known once it has run - "Added 47 mods"
    /// where 47 is what was left after the ones already in the profile were skipped - and returns
    /// null when it changed nothing, which is what keeps an empty move from offering an undo.
    /// </summary>
    /// <remarks>
    /// The undo is the whole draft, not the individual rows: a snapshot restores exactly what was
    /// there, whatever the move did to it, and one mechanism then covers every bulk action on the
    /// page including the ones that only change versions. What it does not restore is the selection,
    /// deliberately - undoing a move is about the profile, and re-picking two hundred rows the user
    /// has since let go of would be a second surprise on top of the first.
    /// </remarks>
    private void RunBulk(Func<string?> change)
    {
        var before = Snapshot();
        string? description = null;

        InBulk(() => description = change());

        if (description is not null)
        {
            // A toast with a way back, which lasts as long as any toast with a link does. The draft
            // it would restore goes stale the moment anything else changes, which is what actually
            // retires it most of the time - see RetireUndo.
            _undoToast = _toasts.Show(
                description,
                ToastSeverity.Info,
                new ToastAction("Undo", () => InBulk(() => RestoreDraft(before))));
        }
    }

    /// <summary>
    /// Takes the offer to undo the last bulk move down. Anything at all having changed retires it: the
    /// draft it holds was an undo for the move that had just happened, and one edit later it is a way
    /// of throwing that edit away.
    /// </summary>
    private void RetireUndo()
    {
        _undoToast?.Dismiss();
        _undoToast = null;
    }

    private IReadOnlyList<ProfileModPin> Snapshot() => [.. Pinned.Select(x => x.Pin)];

    /// <summary>Makes the draft equal to a set of pins, rebuilding every row from the catalog.</summary>
    private void RestoreDraft(IReadOnlyList<ProfileModPin> pins)
    {
        foreach (var row in Pinned)
        {
            row.PropertyChanged -= OnPinnedRowChanged;
        }

        Pinned.Clear();

        foreach (var pin in pins)
        {
            Pinned.Add(CreatePinnedRow(pin));
        }
    }

    [RelayCommand]
    private void Remove(ProfileModRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.PropertyChanged -= OnPinnedRowChanged;

        Pinned.Remove(row);
    }

    /// <summary>
    /// What a bulk move says it did, or null for one that did nothing. Every one of these reads as a
    /// completed act rather than as an instruction, because it is shown next to the button that would
    /// take it back.
    /// </summary>
    private static string? Describe(string verb, int count) => count switch
    {
        <= 0 => null,
        1 => $"{verb} 1 mod",
        _ => $"{verb} {count} mods"
    };

    /// <summary>
    /// What everything that can change the draft asks. Read-only rather than "not saving", because
    /// the save that locks this page may have been started by a page that no longer exists - see
    /// <see cref="IsReadOnly"/>.
    /// </summary>
    private bool NotReadOnly() => IsReadOnly is false;

    #endregion


    #region Reviewing the draft

    /// <summary>
    /// Swaps the two lists for what the draft would change, and back. Reading is not writing, so it
    /// needs no <c>CanEdit</c> guard - and it is allowed while a save runs, which is what the review
    /// is for.
    /// </summary>
    /// <remarks>
    /// <b>Not a step Save waits for.</b> A review somebody has to click through before every save is
    /// friction, and the one place a save's consequences are dangerous - the re-apply - already has
    /// its own control. This is here to be looked at when the draft has grown past what fits in the
    /// head, and to be where a save that imports is watched.
    /// </remarks>
    [RelayCommand]
    private void ToggleReview() => IsReviewing = IsReviewing is false;

    partial void OnIsReviewingChanged(bool value)
    {
        if (value)
        {
            RebuildChanges();
        }
        else
        {
            _changeRows = [];
            ChangesView = null;
        }
    }

    /// <summary>
    /// Takes one change back: the mod goes to what the saved profile holds - out again if it was added,
    /// back in if it was taken out, and to its saved version and lock if it was moved.
    /// </summary>
    /// <remarks>
    /// Its own recount, and no undo toast: the revert is itself the undo, and what it offers to take
    /// back is one row of a list that has just been redrawn.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private void RevertChange(DraftChangeViewModel? change)
    {
        if (change is null)
        {
            return;
        }

        var saved = _original.FirstOrDefault(x => x.ModId == change.ModId);

        InBulk(() =>
        {
            var row = FindPinned(change.ModId);

            if (saved is null)
            {
                if (row is not null)
                {
                    row.PropertyChanged -= OnPinnedRowChanged;

                    Pinned.Remove(row);
                }

                return;
            }

            if (row is null)
            {
                Pinned.Add(CreatePinnedRow(saved));

                return;
            }

            row.SetVersion(saved.VersionId);
            row.LockedByProfile = saved.Lock.ByProfile;

            // A version the row's selector does not offer cannot be moved to, and a draft that
            // quietly stayed where it was would be a revert that did nothing.
            if (row.SelectedVersion.Version.VersionId != saved.VersionId)
            {
                row.PropertyChanged -= OnPinnedRowChanged;

                Pinned.Remove(row);
                Pinned.Add(CreatePinnedRow(saved));
            }
        });
    }

    /// <summary>
    /// What the draft changes, mod by mod, from the same comparison the history page reads - so the
    /// two cannot disagree about what a change is, and this cannot disagree with what a save writes
    /// (a test holds the two answers together).
    /// </summary>
    private IReadOnlyList<ProfileModChange> DraftChanges()
    {
        var before = _original
            .Select(x => new PinnedMod(
                VersionsFor(x.ModId).FirstOrDefault(v => v.VersionId == x.VersionId)
                    ?? Placeholder(x.ModId, x.VersionId),
                x.Lock))
            .ToList();

        var after = Pinned
            .Select(x => new PinnedMod(x.SelectedVersion.Version, x.Lock))
            .ToList();

        return ProfileRevisionComparison.Between(_basedOn, _basedOn, before, after).Changes;
    }

    /// <summary>
    /// Redraws the review from the draft. It is independent of the sources, the filters and the search
    /// by construction - it is read from the draft and the catalog's memory of versions, never from
    /// what the left list happens to be composed of.
    /// </summary>
    private void RebuildChanges()
    {
        var rows = DraftChanges().Select(BuildChangeRow).ToList();

        if (_markedRun is not null)
        {
            StampMarks(rows, _markedRun);
        }

        foreach (var row in rows)
        {
            (row.Group, row.GroupRank) = GroupOf(row);
        }

        rows = [.. rows
            .OrderBy(x => x.GroupRank)
            .ThenBy(x => x.Name, NaturalOrder.Comparer)];

        _changeRows = rows;

        var view = new ListCollectionView(rows);

        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DraftChangeViewModel.Group)));

        ChangesView = view;
    }

    private DraftChangeViewModel BuildChangeRow(ProfileModChange change)
    {
        var item = _itemFactory.Create(_repo.Id, change.Version);

        item.IsSelectable = false;
        item.ShowVersion = false;
        item.OutlineStatus = true;
        item.ShowAdapterLock = false;
        item.Touch = ProfileModTouches.Of(change);
        item.TouchTooltip = ProfileModTouches.Describe(change);
        item.Detail = ProfileModChangeViewModel.Describe(change);

        // The one thing a save has to do before it can write the change, said where the change is read.
        item.Status = change.Kind is not ProfileModChangeKind.Removed && change.Version.IsOnServer is false
            ? ModDisplayStatus.ImportsOnSave
            : ModDisplayStatus.None;

        return new DraftChangeViewModel(change, item);
    }

    /// <summary>
    /// Which group a row sits under. What could not be imported comes first, because it is the one thing
    /// here that wants an answer; the rest follow the order a diff reads in.
    /// </summary>
    private static (string Name, int Rank) GroupOf(DraftChangeViewModel row)
    {
        if (row.Item.HasImportProblem)
        {
            return ("Could not be imported", 0);
        }

        return row.Change.Kind switch
        {
            ProfileModChangeKind.Added => ("Added", 1),
            ProfileModChangeKind.Changed => ("Changed", 2),
            _ => ("Taken out", 3)
        };
    }

    /// <summary>Copies the run's per-version reports onto the review's rows.</summary>
    private static void StampMarks(IEnumerable<DraftChangeViewModel> rows, ProfileSaveRun run)
    {
        var items = rows.ToDictionary(x => x.Item.Mod.Identity, x => x.Item);

        foreach (var progress in run.Progress)
        {
            if (items.TryGetValue(progress.Identity, out var item))
            {
                item.Apply(progress);
            }
        }

        foreach (var result in run.Results)
        {
            if (items.TryGetValue(result.Identity, out var item))
            {
                item.Apply(result);
            }
        }
    }

    #endregion


    #region Updates

    /// <summary>
    /// Applies every update the profile is allowed to take, and says how many it left. Locked mods
    /// are not candidates rather than candidates the save asks about, so the save that follows cannot
    /// contain an unintended version change and needs no prompt at all.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyAllUpdates))]
    private void ApplyAllUpdates() => ApplyUpdates(_updates.Available);

    private bool CanApplyAllUpdates() => ApplicableUpdateCount > 0 && IsReadOnly is false;

    /// <summary>
    /// The half of the same move that costs no upload, one click further in than the primary.
    /// </summary>
    /// <remarks>
    /// Behind the caret rather than beside it because the common errand is catching up to everything
    /// that is newer, wherever the file happens to be; this is for somebody who does not want to
    /// spend an upload right now. The caret carries its own enabled condition and appears only where
    /// the two counts genuinely differ - a menu offering the same thing as the button beside it is a
    /// menu nobody needs to open.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanApplyRepoUpdates))]
    private void ApplyRepoUpdates() => ApplyUpdates([.. _updates.Available.Where(x => x.ImportsOnSave is false)]);

    private bool CanApplyRepoUpdates() => FreeUpdateCount > 0 && IsReadOnly is false;

    private void ApplyUpdates(IReadOnlyList<ProfileModUpdate> updates)
    {
        RunBulk(() =>
        {
            var moved = 0;

            foreach (var update in updates)
            {
                if (FindPinned(update.ModId) is ProfileModRowViewModel row)
                {
                    row.SetVersion(update.To);

                    moved++;
                }
            }

            return Describe("Updated", moved);
        });
    }

    /// <summary>
    /// The same move, over the picked rows instead of over all of them. Locked mods are left alone
    /// here as they are in the batch action - a lock is not a question the selection gets to answer -
    /// and the count of what was skipped goes into what the undo bar says happened.
    /// </summary>
    /// <summary>
    /// What the right-hand bar's Update button says. Names the locked pins it will leave where they are - "Update
    /// (skip 2 locked)" - so the button never promises an update it is not going to make. Only pins that have
    /// an update to take are counted: a locked mod with nothing newer is not being skipped, it is just not
    /// part of this.
    /// </summary>
    public string UpdateSelectedText
    {
        get
        {
            var skipped = PinnedSelection.Picked().OfType<ProfileModRowViewModel>().Count(x => x.HasUpdate && x.IsLocked);

            return skipped > 0 ? $"Update (skip {skipped} locked)" : "Update";
        }
    }

    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private void UpdateSelected()
    {
        RunBulk(() =>
        {
            var moved = 0;
            var skipped = 0;

            foreach (var row in PinnedSelection.Picked().OfType<ProfileModRowViewModel>())
            {
                if (row.UpdateTo is not ModVersionKey target)
                {
                    continue;
                }

                if (row.IsLocked)
                {
                    skipped++;

                    continue;
                }

                row.SetVersion(target);

                moved++;
            }

            return (Describe("Updated", moved), skipped) switch
            {
                (null, 0) => null,
                (null, var left) => $"Nothing updated - {Locked(left)}",
                (var text, 0) => text,
                (var text, var left) => $"{text}, {Locked(left)}"
            };
        });
    }

    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private void LockSelected() => SetLockOnSelected(true);

    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private void UnlockSelected() => SetLockOnSelected(false);

    /// <summary>
    /// Holds or releases the picked pins. Only the profile's own flag moves: the adapter's is a fact
    /// about the mod file that no page may overwrite, so a row it holds stays locked and is simply
    /// not one of the rows this changed.
    /// </summary>
    private void SetLockOnSelected(bool locked)
    {
        RunBulk(() =>
        {
            var changed = 0;

            foreach (var row in PinnedSelection.Picked().OfType<ProfileModRowViewModel>())
            {
                if (row.LockedByProfile == locked)
                {
                    continue;
                }

                row.LockedByProfile = locked;

                changed++;
            }

            return Describe(locked ? "Locked" : "Unlocked", changed);
        });
    }

    private static string Locked(int count) => count == 1 ? "1 was locked and skipped" : $"{count} were locked and skipped";

    /// <summary>
    /// One row's update. Locked here means the move is a deliberate act on this row, carrying the
    /// reason the lock is there.
    /// </summary>
    [RelayCommand]
    private async Task UpdateOne(ProfileModRowViewModel? row)
    {
        if (row?.UpdateTo is not ModVersionKey target)
        {
            return;
        }

        if (row.Versions.FirstOrDefault(x => x.Version.VersionId == target) is not ProfileModVersionOption option)
        {
            return;
        }

        if (row.IsLocked && await ConfirmLockedVersionChangeAsync(row, option) is false)
        {
            return;
        }

        row.SetVersion(target);
    }

    /// <summary>
    /// The locked mods the batch left alone, with an unchecked box each. For someone who genuinely
    /// does mean to move them, rather than the standing cost of the common action.
    /// </summary>
    [RelayCommand]
    private async Task ShowSkippedUpdates()
    {
        if (_updates.Skipped.Count == 0)
        {
            return;
        }

        var modal = new ProfileLockedUpdatesModalViewModel([.. _updates.Skipped
            .Select(x => new ProfileLockedUpdateViewModel(FindPinned(x.ModId)?.Name ?? x.ModId.Value, x))]);

        await _modalService.Show(modal);

        foreach (var modId in modal.Result)
        {
            if (_updates.Skipped.FirstOrDefault(x => x.ModId == modId) is ProfileModUpdate update)
            {
                FindPinned(modId)?.SetVersion(update.To);
            }
        }
    }

    /// <summary>
    /// A link to the filter that isolates them, exactly like the skipped-locked count above - the set
    /// is answered at save, one version at a time, so there is nothing here for a bulk action to do.
    /// </summary>
    [RelayCommand]
    private void ShowAmbiguousVersions() => AvailableFilter = AvailableModFilter.Unordered;

    /// <summary>
    /// The pinned mods a remote source has something newer of, which the Updates filter takes in along
    /// with the ones here - each row's own link is how they are got.
    /// </summary>
    [RelayCommand]
    private void ShowRemoteUpdates() => PinnedFilter = PinnedModFilter.Updates;

    /// <summary>
    /// Carries why the mod is locked, because that is the part that decides the answer - and words
    /// the profile lock as being about this profile, which is the only scope it has.
    /// </summary>
    /// <remarks>
    /// <b>The question is about the mod, not about the profile.</b> It used to ask whether to "move
    /// this profile to version X", which names the wrong subject - a profile is not the thing that
    /// moves - and reads as a change to the whole list. It is also reached from the version selector,
    /// where the target may well be older than the pin, so the verb has to be one that covers both
    /// directions.
    /// </remarks>
    private async Task<bool> ConfirmLockedVersionChangeAsync(ProfileModRowViewModel row, ProfileModVersionOption target)
    {
        var reason = row.Lock.Source switch
        {
            ProfileModLockSource.Adapter =>
                "The game adapter reads it as version-sensitive - a map, typically - so changing its version "
                    + "partway through a save can corrupt that save.",
            ProfileModLockSource.Profile =>
                "You locked it in this profile. Other profiles are not affected either way.",
            _ =>
                "The game adapter reads it as version-sensitive and you have locked it in this profile as well. "
                    + "Changing its version partway through a save can corrupt that save.",
        };

        var confirmation = new ConfirmationDialogViewModel(
            "This mod is locked",
            $"'{row.Name}' is pinned at {row.SelectedVersion.Version.VersionId} and locked.\n\n"
                + $"{reason}\n\n"
                + $"Change '{row.Name}' to {target.Version.VersionId} in this profile?",
            IconKind.Warning,
            "Change the version",
            "Leave it alone");

        await _modalService.Show(confirmation);

        return confirmation.Result;
    }

    #endregion


    #region Saving

    /// <summary>
    /// Hands the save to <see cref="ProfileSaveService"/> and watches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole of the save moved out.</b> Import, revision, re-apply and drift check belong to
    /// the profile rather than to this page: navigating away used to dispose the page and its catalog
    /// mid-flight, and the files finished registering while the revision was never written and
    /// nothing said so. What is left here is the snapshot, the marks on the rows and the sentence
    /// afterwards.
    /// </para>
    /// <para>
    /// <b>The snapshot is taken before the import rather than re-read from the draft after it</b>, so
    /// a row added during an upload is not saved without having been imported and a source toggled
    /// during one cannot replace what is being written.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveChanges()
    {
        // Read and cleared here, so the decision cannot outlive the save that carried it.
        var apply = _skipApplyOnce is false;
        _skipApplyOnce = false;

        var pending = Pinned.Where(x => x.IsPending).ToList();

        // What the last save said about these rows does not carry over to this one. Where there is
        // something to import it is watched in the review; a save with nothing to upload is over
        // before there would be anything to watch, and a flicker between views is worse than none.
        _markedRun = null;

        if (pending.Count > 0)
        {
            IsReviewing = true;
        }

        foreach (var row in _changeRows)
        {
            row.Item.ResetImportState();
        }

        var request = new ProfileSaveRequest(
            _repo,
            _profile.Id,
            _profile.Name,
            _basedOn,
            VersionDescription,
            _original,
            [.. Pinned.Select(x => x.Pin)],
            [.. _originalIgnored],
            [.. DesiredIgnored()],
            [.. pending.Select(x => x.SelectedVersion.Version)],
            pending.ToDictionary(x => x.SelectedVersion.Version.Identity, x => x.Name),
            _catalog,
            apply && HasModListChanges);

        RetireActivationOffer();

        var run = _saveService.Start(request);

        run.Advanced += OnRunAdvanced;

        try
        {
            await WatchAsync(run.Completion, request);
        }
        finally
        {
            run.Advanced -= OnRunAdvanced;
        }
    }

    private bool CanSave() => HasUnsavedChanges && IsReadOnly is false && CanImportHere;

    /// <summary>
    /// Follows one save to its end and reports it, whether this page started it or found it already
    /// running.
    /// </summary>
    /// <remarks>
    /// The page is read-only for the whole of it, and what it does afterwards depends only on the
    /// outcome - which is what makes rejoining a save in progress the same code path as starting one.
    /// </remarks>
    private async Task WatchAsync(Task<ProfileSaveOutcome> completion, ProfileSaveRequest request)
    {
        IsSaving = true;
        IsReadOnly = true;

        try
        {
            // What it says is the save service's to say - see ProfileSaveService.Start - so this only
            // acts on how it went.
            var outcome = await completion;

            if (outcome.Succeeded is false)
            {
                // Deliberately not reloaded, and the baseline deliberately not moved on. A reload
                // rebuilds both lists from the server, and the rows that could not be imported are
                // not on the server - they would vanish from the profile without the user being told
                // which ones, having just been told that something went wrong. Leaving the draft
                // where it is also keeps it unsaved, so Save stays enabled and pressing it again once
                // the cause is fixed is the whole recovery path.
                Recount();

                return;
            }

            _basedOn = outcome.Revision;
            _profile.HeadRevision = outcome.Revision;

            // The repo holds them now, and the import invalidated the catalog on its way out - so
            // the reload below reads them from the registered half, where they belong.
            _adopted = [];

            // It described the save that just happened, not the next one. Left in place it would be
            // carried onto an unrelated edit ten minutes later, which is how a history fills with
            // labels that are quietly wrong.
            if (outcome.RevisionWritten)
            {
                VersionDescription = "";
            }

            await ReloadAsync();

            // Nothing is left to review, and the recount the reload ran has already said so; this is for
            // a save whose reload could not run.
            IsReviewing = false;

            if (request.Apply && outcome.ApplyMessage is null)
            {
                OfferActivation();
            }
        }
        finally
        {
            IsSaving = false;
            IsReadOnly = false;

            if (_recomposeWhenSaved)
            {
                _recomposeWhenSaved = false;

                await RecomposeAsync();
            }
        }
    }

    /// <summary>
    /// Rejoins a save that was already running when this page was built, and does the post-save
    /// reload it would have done anyway once it finishes.
    /// </summary>
    /// <remarks>
    /// The draft is the service's snapshot rather than the server's list, because the server has not
    /// been told about it yet. The marks come from the run's own progress, so an editor that opens
    /// half way through an upload shows where the upload has got to rather than a blank list.
    /// </remarks>
    private async Task RejoinAsync(ProfileSaveRun run)
    {
        run.Advanced += OnRunAdvanced;

        // Before the catalog is composed, so the pending rows the draft is about to bring in resolve
        // against the files the save is uploading rather than against a catalog that has never heard
        // of them.
        _adopted = run.Request.Pending;

        IsLoading = true;

        try
        {
            var snapshot = await _catalog.GetAsync(_cancellation.Token);

            await ReadAddedDatesAsync(run.Request.BasedOn);

            await OnUiThreadAsync(() =>
            {
                Compose(snapshot);
                AdoptDraft(run);
            });

            await WatchAsync(run.Completion, run.Request);
        }
        catch (OperationCanceledException)
        {
            // Navigating away while the catalog was still being read.
        }
        finally
        {
            run.Advanced -= OnRunAdvanced;
        }
    }

    /// <summary>
    /// The dates the list the save started from carried, for a page that did not load that list itself.
    /// </summary>
    /// <remarks>
    /// Best effort, and on purpose: a date is a nicety on a page that is otherwise a spectator of a save
    /// that is already running, and a failed read must not stop it from showing the save. Without them
    /// every row would read as added at the moment the page opened, which is wrong rather than merely
    /// missing - so the rows fall back to that only when this could not be asked.
    /// </remarks>
    private async Task ReadAddedDatesAsync(int revision)
    {
        try
        {
            var modList = await _dependenciesClient.GetModDependenciesV1Async(
                _repo.Id, _profile.Id, revision, _cancellation.Token);

            _originalAdded = ReadAdded(modList);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _originalAdded = [];
        }
    }

    /// <summary>Draws the list the save is writing, with whatever it has already reported on it.</summary>
    private void AdoptDraft(ProfileSaveRun run)
    {
        _publishing = true;

        try
        {
            _basedOn = run.Request.BasedOn;
            _original = run.Request.Original;
            _originalIgnored = [.. run.Request.IgnoredOriginal];
            _ignoredMods = [.. run.Request.IgnoredDesired];

            foreach (var row in Pinned)
            {
                row.PropertyChanged -= OnPinnedRowChanged;
            }

            Pinned.Clear();

            foreach (var pin in run.Request.Desired)
            {
                Pinned.Add(CreatePinnedRow(pin));
            }
        }
        finally
        {
            _publishing = false;
        }

        Recount();

        // A save with something to import is watched in the review, so a page that arrives half way
        // through one opens there.
        if (run.Request.Pending.Count > 0)
        {
            IsReviewing = true;
        }

        MarkFromRun(run);
    }

    private void OnRunAdvanced()
    {
        // The import runs off the UI thread and these are bound rows.
        _ = OnUiThreadAsync(() =>
        {
            if (_saveService.Find(_profile.Id) is ProfileSaveRun run)
            {
                MarkFromRun(run);
            }
        });
    }

    /// <summary>
    /// Copies the run's per-version reports onto the review's rows. The run reports per version
    /// rather than writing into row view models it does not own, which is what lets a second page be
    /// built for the same save without the first one's rows being written into from two places.
    /// </summary>
    /// <remarks>
    /// Onto the review and not the lists: a save that imports is watched there, and the lists' rows
    /// are the profile's own and stay as they are. Remembered, so a review that is not open yet - or
    /// is rebuilt - is told what has already happened.
    /// </remarks>
    private void MarkFromRun(ProfileSaveRun run)
    {
        _markedRun = run;

        if (IsReviewing)
        {
            StampMarks(_changeRows, run);
        }
    }

    /// <summary>
    /// The variant, one click further in than the primary and only offered where there is something
    /// to skip. It arms the flag and runs the same save, so there is no second code path and nothing
    /// left switched on afterwards.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveOnly))]
    private Task SaveOnly()
    {
        _skipApplyOnce = true;

        return SaveChangesCommand.ExecuteAsync(null);
    }

    private bool CanSaveOnly() => CanSave() && WillApply;

    /// <summary>
    /// Stops the save this page is watching. The same act as the strip's own Cancel, because it is
    /// the same cancellation source - and it works from either side of a navigation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopSaving))]
    private void StopSaving()
    {
        _saveService.Find(_profile.Id)?.Cancel?.Invoke();
    }

    private bool CanStopSaving() => IsSaving;

    /// <summary>
    /// The onboarding case: a profile nothing is using yet. Naming the game because here that
    /// genuinely is a choice, and offered afterwards rather than folded into the save.
    /// </summary>
    /// <remarks>
    /// A toast rather than a bar: it lapses on its own, which is the whole of "not now". It names no
    /// game, because a repo is about one and there is nothing to choose between.
    /// </remarks>
    private void OfferActivation()
    {
        RetireActivationOffer();

        if (_repo.Games.FirstOrDefault() is not Game game)
        {
            return;
        }

        _activationToast = _toasts.Show(
            "Do you want to activate this profile?",
            ToastSeverity.Info,
            new ToastAction("Activate", () => _ = AcceptActivationOfferAsync(game)));
    }

    private void RetireActivationOffer()
    {
        _activationToast?.Dismiss();
        _activationToast = null;
    }

    private async Task AcceptActivationOfferAsync(Game game)
    {
        try
        {
            var outcome = await _applyService.ActivateAsync(
                _repo,
                game,
                _profile.Id,
                _profile.Name,
                // A mode change, not a re-apply: what the previous profile put in that folder comes back
                // out, so the reconciler's plan is the confirmation.
                confirmPlan: true,
                progress: null,
                CancellationToken.None);

            if (outcome.Activated)
            {
                RefreshApplyTargets();
            }

            _toasts.Show(outcome.Message, outcome.ToastSeverity);

            await _driftMonitor.CheckAsync();
        }
        catch (Exception exception)
        {
            // Nothing awaits a toast's link, so a failure here has no command to carry it to the
            // global handler and has to reach the user itself.
            await _errorReporter.ShowAsync(exception, $"putting '{_profile.Name}' on '{game.Name}'");
        }
    }

    /// <summary>
    /// Throws the draft away. This is what makes importing on save rather than on drag worth doing:
    /// nothing pending has been uploaded, so there is nothing in the repo to clean up.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private async Task DiscardChanges()
    {
        var confirmation = new ConfirmationDialogViewModel(
            "Discard changes?",
            "The mods waiting to be imported have not been uploaded, so nothing in the repo changes.",
            IconKind.Question,
            "Discard",
            "Keep editing");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        await ReloadAsync();
    }

    private bool CanDiscard() => HasUnsavedChanges && IsReadOnly is false;

    #endregion


    #region Bringing a list in from somewhere else

    /// <summary>
    /// Starts this profile's list from another one's. The fastest way to build a profile is almost
    /// never to pick its mods one at a time - it is to take the profile next to it and change what
    /// differs.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private async Task CopyFromProfile()
    {
        try
        {
            var profiles = await _profilesClient.GetProfilesV1Async(_repo.Id, _cancellation.Token);

            var others = profiles
                .Where(x => x.Id != _profile.Id)
                .OrderBy(x => x.Name, NaturalOrder.Comparer)
                .ToList();

            var modal = new CopyProfileModsModalViewModel(others, ProfileName);

            await _modalService.Show(modal);

            if (modal.Result is not ProfileDto source)
            {
                return;
            }

            var list = await _dependenciesClient.GetModDependenciesV1Async(
                _repo.Id, source.Id, null, _cancellation.Token);

            // Read at that profile's head. This is a copy of what it holds now, not a link to it -
            // the two lists have nothing to do with each other after this.
            var pins = list.Dependencies
                .Select(x => new ProfileModPin(
                    ModKey.From(x.ModId),
                    ModVersionKey.From(x.ModVersionId),
                    new ProfileModLock(false, x.Locked)))
                .ToList();

            CopyIn(source.Name, modal.ResultMode, pins);
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-request.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "copying a mod list from another profile");
        }
    }

    private void CopyIn(string sourceName, CopyProfileModsMode mode, IReadOnlyList<ProfileModPin> pins)
    {
        if (mode is CopyProfileModsMode.Replace)
        {
            RunBulk(() =>
            {
                RestoreDraft(pins);

                return pins.Count == 1
                    ? $"This profile now holds the one mod {sourceName} does."
                    : $"This profile now holds the {pins.Count} mods {sourceName} does.";
            });

            return;
        }

        var added = 0;
        var already = 0;

        RunBulk(() =>
        {
            foreach (var pin in pins)
            {
                // Read before the bulk began, which is what it should be: a profile holds one pin
                // per mod, so nothing added inside this loop can collide with anything else in it.
                if (_pinnedIds.Contains(pin.ModId))
                {
                    already++;

                    continue;
                }

                Pinned.Add(CreatePinnedRow(pin));

                added++;
            }

            // Nothing added means nothing to undo, so the sentence goes out on its own below.
            return added == 0
                ? null
                : already == 0
                    ? $"Copied {added} {Mods(added)} from {sourceName}."
                    : $"Copied {added} {Mods(added)} from {sourceName}. {already} {Were(already)} already here.";
        });

        if (added == 0)
        {
            _toasts.Show($"Nothing to copy - this profile already holds everything {sourceName} does.");
        }
    }

    /// <summary>
    /// Turns a pasted list into a selection on the left. It picks and reports; it never adds - see
    /// <see cref="PasteModListModalViewModel"/> for why the step in between is the point.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private async Task PasteList()
    {
        var modal = new PasteModListModalViewModel();

        await _modalService.Show(modal);

        if (modal.Result.Count > 0)
        {
            ApplyPastedList(modal.Result);
        }
    }

    private void ApplyPastedList(IReadOnlyList<string> terms)
    {
        // Cleared first, so what was matched is on screen to look at. A selection made behind a
        // search that hides it is a report with nothing to read.
        SearchText = string.Empty;
        AvailableFilter = AvailableModFilter.All;

        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Pinned)
        {
            pinned.Add(row.ModId.Value);
            pinned.Add(row.Name);
        }

        // Ids win over names, and the first row to claim either keeps it. Matching is exact in both:
        // a fuzzy match here would quietly pick the wrong mod, and "not found" is the better answer.
        var byId = new Dictionary<string, ProfileModRowViewModel>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, ProfileModRowViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _available)
        {
            byId.TryAdd(row.ModId.Value, row);
            byName.TryAdd(row.Name, row);
        }

        var matched = new HashSet<ProfileModRowViewModel>();
        var already = 0;
        var missing = new List<string>();

        foreach (var term in terms)
        {
            if (pinned.Contains(term))
            {
                already++;
            }
            else if (byId.TryGetValue(term, out var row) || byName.TryGetValue(term, out row))
            {
                matched.Add(row);
            }
            else
            {
                missing.Add(term);
            }
        }

        // Replaces whatever was picked rather than adding to it: a pasted list is a statement about
        // the whole set somebody wants, not another handful on top of one.
        _publishing = true;

        try
        {
            foreach (var row in _available)
            {
                row.IsSelected = matched.Contains(row);
            }
        }
        finally
        {
            _publishing = false;
        }

        AvailableSelection.Recount();

        _toasts.Show(DescribePaste(terms.Count, matched.Count, already, missing));
    }

    /// <summary>
    /// What a paste found, in one sentence. Names the ones it could not find rather than only
    /// counting them: a list that says "3 were not found" and stops leaves the reader to diff two
    /// lists by eye, which is the work they came here to avoid.
    /// </summary>
    private static string DescribePaste(int asked, int matched, int already, IReadOnlyList<string> missing)
    {
        var text = matched == 0
            ? $"Nothing in the left list matches the {asked} {Names(asked)} you pasted."
            : $"Selected {matched} of the {asked} {Names(asked)} you pasted.";

        if (already > 0)
        {
            text += $" {already} {Were(already)} already in this profile.";
        }

        if (missing.Count > 0)
        {
            var shown = missing.Take(5).ToList();
            var rest = missing.Count - shown.Count;

            text += $" {missing.Count} not found: {string.Join(", ", shown)}";
            text += rest > 0 ? $", and {rest} more." : ".";
        }

        return text;
    }

    private static string Mods(int count) => count == 1 ? "mod" : "mods";
    private static string Names(int count) => count == 1 ? "name" : "names";
    private static string Were(int count) => count == 1 ? "was" : "were";

    #endregion


    #region Sources

    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private async Task RescanAll()
    {
        _catalog.RescanAll();

        await RecomposeAsync();
    }

    /// <summary>
    /// Adds a folder for this session only. Someone building a profile out of a USB stick should not
    /// have that folder haunting the list for months, so nothing about it is written to disk.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private async Task AddSource()
    {
        if (_dialogService.PickFolder(null) is not string path)
        {
            return;
        }

        _catalog.AddAdHocSource(path);

        await RecomposeAsync();
    }

    /// <summary>
    /// Reads another profile in this repo as a source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A membership set and a version per mod, not a scan.</b> A profile's pins are registered
    /// versions by foreign key, so the catalog already holds every one of them - what this adds is
    /// which of them that profile names. Switch the repo chip off and a profile chip on and the left
    /// list is exactly what that profile has and this one does not, which is a diff no other part of
    /// the app can show.
    /// </para>
    /// <para>
    /// <b><em>Copy from a profile…</em> stays</b>, and this does not replace it. A source can only
    /// add; <em>Replace</em> and the removals it implies are a statement about the whole list, which
    /// no per-row action expresses.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private async Task AddProfileSource()
    {
        try
        {
            var profiles = await _profilesClient.GetProfilesV1Async(_repo.Id, _cancellation.Token);

            var others = profiles
                .Where(x => x.Id != _profile.Id)
                .Where(x => _profileSources.Any(source => source.ProfileId == x.Id) is false)
                .OrderBy(x => x.Name, NaturalOrder.Comparer)
                .ToList();

            var modal = new PickProfileSourceModalViewModel(others);

            await _modalService.Show(modal);

            if (modal.Result is not ProfileDto picked)
            {
                return;
            }

            var list = await _dependenciesClient.GetModDependenciesV1Async(
                _repo.Id, picked.Id, null, _cancellation.Token);

            // Read at that profile's head, like the copy action. This is what it holds now, not a
            // link to it.
            _profileSources.Add(new ProfileModSource(
                picked.Id,
                new ModSource(
                    ModSourceId.ForProfile(picked.Id),
                    picked.Name,
                    "Another profile in this repo.",
                    ModSourceKind.Profile),
                [.. list.Dependencies.Select(x => new ProfileModPin(
                    ModKey.From(x.ModId),
                    ModVersionKey.From(x.ModVersionId),
                    new ProfileModLock(false, x.Locked)))]));

            await RecomposeAsync();
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-request.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "reading another profile's mod list as a source");
        }
    }

    [RelayCommand(CanExecute = nameof(NotReadOnly))]
    private async Task RemoveSource(ModSourceViewModel? source)
    {
        if (source is null || source.CanRemove is false)
        {
            return;
        }

        if (source.IsProfile)
        {
            _profileSources.RemoveAll(x => x.Source.Id == source.Source.Id);
        }
        else
        {
            _catalog.RemoveAdHocSource(source.Source.Id);
        }

        await RecomposeAsync();
    }

    private void OnSourceEnabledChanged(ModSourceViewModel source, bool enabled)
    {
        if (source.IsRepo)
        {
            _includeRegistered = enabled;
        }
        else if (source.IsProfile)
        {
            if (_profileSources.FirstOrDefault(x => x.Source.Id == source.Source.Id) is ProfileModSource profile)
            {
                profile.IsEnabled = enabled;
            }
        }
        else if (source.IsRemote)
        {
            if (_remoteSources.FirstOrDefault(x => x.Id == source.Source.Id) is RemoteModSourceState remote)
            {
                remote.IsEnabled = enabled;

                // Switching it off and on again is how a lookup that failed, or that the server could
                // not yet vouch for, is asked again. An answer that stood is kept: it is a page's worth
                // old at most, and asking again would only cost a round trip.
                if (enabled && (remote.Error is not null || remote.IsIncomplete))
                {
                    remote.Forget();
                }
            }

            // Nothing in the catalog or the lists changes, only the links on the rows - so this is
            // not a recompose.
            RefreshRemoteOffers();

            return;
        }
        else
        {
            _catalog.SetEnabled(source.Source, enabled);
        }

        // Recomposes from the scans already in memory, so this is instant for a source that has been
        // read once - which is the whole reason the catalog caches per source. It rebuilds what the
        // catalog decides and nothing the user has: the draft, the selections and the removals all
        // survive it.
        _ = RecomposeAsync();
    }

    #endregion


    #region Remote sources

    private ModSourceViewModel CreateRemoteChip(RemoteModSourceState remote)
    {
        return new ModSourceViewModel(
            new ModSourceStatus(remote.Source, remote.IsEnabled, remote.OfferCount, remote.Error),
            OnSourceEnabledChanged,
            isBusy: remote.IsEnabled && remote.IsLookingUp,
            hasCount: remote.IsEnabled && remote.HasAnswered);
    }

    /// <summary>
    /// Works the offers out again and puts them on every row and chip - for a lookup answering or a
    /// chip being switched, where nothing else about the lists has changed.
    /// </summary>
    private void RefreshRemoteOffers()
    {
        ComputeRemoteOffers();

        foreach (var row in Pinned)
        {
            row.RemoteOffer = _remoteOffers.GetValueOrDefault(row.ModId);
        }

        foreach (var row in _available)
        {
            row.RemoteOffer = _remoteOffers.GetValueOrDefault(row.ModId);
        }

        // The Updates filter and the band's count both read the links, so they are counted again - and
        // the filtered view re-run, which a CollectionView does not do for a property it cannot see.
        // Not a full Recount: that retires the bulk undo, and a lookup landing in the background is not
        // something somebody did.
        RecountRemoteUpdates();
        PinnedView.Refresh();
        RecountPinnedVisible();
        PinnedSelection.Recount();
    }

    /// <summary>
    /// Against the pinned rows, not the offers: an offer for a mod only in a folder is the left list's
    /// business, and it is the profile's updates the band is about.
    /// </summary>
    private void RecountRemoteUpdates()
    {
        RemoteUpdateCount = Pinned.Count(x => x.RemoteOffer is not null && x.IsLocked is false);
        RemoteLockedUpdateCount = Pinned.Count(x => x.RemoteOffer is not null && x.IsLocked);
    }

    /// <summary>
    /// Which newer versions the enabled remote sources have of the mods known here, and asks them about
    /// any mod they have not been asked about yet.
    /// </summary>
    /// <remarks>
    /// <b>Every known mod, not only the pinned ones.</b> The left list is where somebody looks for
    /// something to add, and a mod sitting in a folder at an old version is as worth a link as one
    /// the profile already pins. Asked incrementally, so switching a folder on later asks only about
    /// what it brought.
    /// </remarks>
    private void ComputeRemoteOffers()
    {
        var offers = new Dictionary<ModKey, RemoteOfferViewModel>();

        foreach (var remote in _remoteSources)
        {
            remote.OfferCount = 0;

            if (remote.IsEnabled is false)
            {
                continue;
            }

            var newer = RemoteModOffers.Newer(remote.Answers.Values, _versionsByMod, _repo.Adapter.VersionComparer);

            remote.OfferCount = newer.Count;

            foreach (var (modId, offer) in newer)
            {
                // The first enabled source to have something newer wins the row; a row has room for
                // one link, and two sources disagreeing about the newest is not a question this asks.
                offers.TryAdd(modId, new RemoteOfferViewModel(offer, remote.Remote.DisplayName, OpenRemoteOffer));
            }

            LookUpUnasked(remote);
        }

        _remoteOffers = offers;

        RefreshRemoteChips();
    }

    /// <summary>
    /// Replaces the remote chips in the row with ones that say what their source says now. Only those:
    /// the rest are rebuilt by a recompose, which a lookup answering is not.
    /// </summary>
    private void RefreshRemoteChips()
    {
        for (var i = 0; i < Sources.Count; i++)
        {
            if (Sources[i].IsRemote && _remoteSources.FirstOrDefault(x => x.Id == Sources[i].Source.Id) is RemoteModSourceState remote)
            {
                Sources[i] = CreateRemoteChip(remote);
            }
        }
    }

    private void LookUpUnasked(RemoteModSourceState remote)
    {
        if (remote.IsLookingUp || remote.Error is not null)
        {
            return;
        }

        var unasked = _versionsByMod.Keys.Where(x => remote.Asked.Contains(x) is false).ToList();

        if (unasked.Count == 0)
        {
            return;
        }

        remote.Asked.UnionWith(unasked);
        remote.IsLookingUp = true;

        _ = LookUpAsync(remote, unasked);
    }

    private async Task LookUpAsync(RemoteModSourceState remote, List<ModKey> mods)
    {
        try
        {
            var lookup = await remote.Remote.LookUpAsync(mods, _cancellation.Token);

            foreach (var offer in lookup.Offers)
            {
                remote.Answers[offer.ModId] = offer;
            }

            remote.HasAnswered = true;
            remote.CurrentAsOf = lookup.CurrentAsOf;
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-request.
            return;
        }
        catch (Exception exception)
        {
            // On the chip rather than in a dialog: this is one optional source failing, which is what
            // a red chip means everywhere else on the row. Its mods count as unasked again.
            remote.Asked.ExceptWith(mods);
            remote.Error = exception is ApiException api
                ? $"The ModsDude server answered {api.StatusCode} when asked what {remote.Remote.DisplayName} has."
                : $"The ModsDude server could not be asked what {remote.Remote.DisplayName} has.";
        }
        finally
        {
            remote.IsLookingUp = false;
        }

        await OnUiThreadAsync(RefreshRemoteOffers);
    }

    /// <summary>
    /// Opens the page a newer version is downloaded from, and switches Downloads on - the file lands
    /// there, and looking for it is the next thing anybody does.
    /// </summary>
    /// <remarks>
    /// Switching the chip on is the same kind of exception the drift notice makes to "navigating reads
    /// no disk": following the link is asking for that file specifically. A rescan once the download has
    /// finished is still the user's, because nothing here can tell when it has.
    /// </remarks>
    private void OpenRemoteOffer(RemoteModOffer offer)
    {
        // The address came from the server; only ever hand the shell a web page.
        if (Uri.TryCreate(offer.PageUrl, UriKind.Absolute, out var uri) is false
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _ = _errorReporter.ShowAsync(exception, "opening the mod's page");

            return;
        }

        if (Sources.FirstOrDefault(x => x.Source.Kind is ModSourceKind.Downloads) is { IsEnabled: false } downloads)
        {
            downloads.IsEnabled = true;
        }
    }

    #endregion


    /// <summary>
    /// Switches on one of a game's mod folders, for a page opened <i>at</i> that folder rather
    /// than merely opened - which today means arriving from the drift notice.
    /// </summary>
    /// <remarks>
    /// Sources are off by default because navigating must not read a disk. Coming here from a drift
    /// notice is not navigating in that sense: the versions the game downloaded are sitting in that
    /// folder and looking at them is the entire reason the user was sent here, so leaving it switched
    /// off would make them find and tick it before the page could answer the question it opened with.
    /// Called before the page is shown, and again if the page is already open when the notice is
    /// clicked - enabling an already-enabled source is a no-op.
    /// </remarks>
    public void ScanTarget(ModTargetRef target)
    {
        _catalog.SetEnabled(ModSourceId.ForTarget(target), true);

        // The repo registers every version this folder could hold, so with it left on, a mod the game
        // removed from disk still shows on the left as if nothing had happened, and "Not in sources"
        // - the filter that finds exactly what this folder no longer offers - selects nothing either.
        // Coming here is coming to look at one folder, so it is switched off exactly as if the user
        // had unticked it themselves, leaving that folder as the only enabled source.
        _includeRegistered = false;

        // The two filters that answer "what did the game do to this folder" from either side: what
        // it added, which is on this computer and unregistered until a save imports it, and what it
        // took away, which with the repo off above is exactly what this profile pins and the folder
        // no longer does.
        AvailableFilter = AvailableModFilter.New;
        PinnedFilter = PinnedModFilter.NotInSources;

        // Only recomposes where the page is already up; during construction there is nothing to
        // recompose and the initial load reads the flag on its way through. A recompose rather than a
        // reload, because arriving here a second time must not throw away whatever the user has been
        // building since the first - and a recompose that lands mid-save defers itself.
        if (IsLoading is false)
        {
            _ = RecomposeAsync();
        }
    }

    public void Dispose()
    {
        _navigationLock.ReleaseLock(this);
        _notices.Release(_activeProfile);
        _leases.Changed -= OnLeasesChanged;
        _syncService.ModFolderChanged -= OnModFolderChanged;

        // The save itself is not stopped: it belongs to the profile, not to this page, and the strip
        // keeps its Cancel. Its outcome is the save service's to report, so nothing is given up here.
        //
        // The two offers are this page's, though: an undo would restore a draft nobody can see any
        // more, and an activation offer belongs to a save that page made.
        RetireUndo();
        RetireActivationOffer();

        foreach (var row in _available)
        {
            row.PropertyChanged -= OnAvailableRowChanged;
        }

        foreach (var row in Pinned)
        {
            row.PropertyChanged -= OnPinnedRowChanged;
        }

        _repo.Games.CollectionChanged -= OnGamesChanged;

        foreach (var game in _watchedGames)
        {
            game.PropertyChanged -= OnGameChanged;
        }

        _watchedGames.Clear();

        // Deliberately not disposed: the wait may still be inside the token's registration, and
        // disposing a source out from under that is not safe. Nothing here holds a wait handle.
        _cancellation.Cancel();
        _catalog.Dispose();
    }


    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshApplyTargets();
    }

    /// <summary>
    /// An apply changed a mod folder, so the scan of it - if this page has one - is a picture of a folder
    /// that is not that folder any more: what the apply installed is missing from it, and what it
    /// recycled is still there, offered as an import whose file no longer exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Any source that is that folder, on standby or not.</b> A chip switched off is a statement about
    /// what is being looked at, not about what has been read - see <see cref="ModCatalog.RescanFolder"/>.
    /// A folder this page never scanned costs nothing and does not recompose.
    /// </para>
    /// <para>
    /// <b>A recompose, never a reload</b>: the draft, the selections and the pending removals are the
    /// user's and stay. One that lands mid-save is held until the save is over, like any other.
    /// </para>
    /// </remarks>
    private void OnModFolderChanged(string folder)
    {
        if (_cancellation.IsCancellationRequested || _catalog.RescanFolder(folder) is false)
        {
            return;
        }

        // Raised from whichever thread ran the apply.
        _ = Application.Current?.Dispatcher.InvokeAsync(RecomposeAsync);
    }

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Game.ActiveProfile))
        {
            RefreshApplyTargets();
        }
    }

    /// <summary>
    /// Derived from the games' own standing intent, every time it could have moved. The count is
    /// what the primary button says, so being a step behind would mislabel it.
    /// </summary>
    private void RefreshApplyTargets()
    {
        foreach (var game in _watchedGames)
        {
            game.PropertyChanged -= OnGameChanged;
        }

        _watchedGames.Clear();

        foreach (var game in _repo.Games)
        {
            game.PropertyChanged += OnGameChanged;
            _watchedGames.Add(game);
        }

        ApplyTarget = _gameRepository.GetGameFollowing(_repo.Scope, _activeProfile);
    }

    /// <summary>
    /// A cancelled scan is the expected outcome of navigating away, not something to show the user an
    /// error modal about.
    /// </summary>
    protected override void OnInitFailed(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return;
        }

        base.OnInitFailed(ex);
    }

    /// <summary>
    /// Asks the service before it asks the server.
    /// </summary>
    /// <remarks>
    /// A page built for a profile that is being saved has to draw the draft the save is holding, not
    /// the list the server still has - the save has not told it yet. It stays read-only until the run
    /// finishes, at which point it does the post-save reload it would have done anyway.
    /// </remarks>
    protected override Task InitAsync()
    {
        return _saveService.Find(_profile.Id) is ProfileSaveRun run
            ? RejoinAsync(run)
            : ReloadAsync();
    }

    /// <summary>
    /// Whether a save started from this page could import what the draft is holding.
    /// </summary>
    /// <remarks>
    /// <b>The refusal is exactly as wide as the lease.</b> A draft with nothing pending is a revision
    /// write and is safe beside any import, so it saves; only a draft with mods to import has to wait
    /// for whatever is already importing into this repo. Being unable to write for a few minutes is
    /// not a reason to be unable to think for a few minutes.
    /// </remarks>
    public bool CanImportHere => PendingCount == 0 || _saveService.DescribeImportBusy(_repo.Id) is null;

    /// <summary>
    /// Why Save is greyed, or null where it is not. Names what is running rather than saying that
    /// something is, because the answer is "wait for that one" and only the name says which.
    /// </summary>
    public string? SaveBlockedReason
    {
        get
        {
            if (PendingCount == 0 || _saveService.DescribeImportBusy(_repo.Id) is not string holder)
            {
                return null;
            }

            var mods = PendingCount == 1 ? "1 mod here needs importing" : $"{PendingCount} mods here need importing";

            return $"{mods}, and {holder.ToLowerInvariant()}.";
        }
    }

    public bool HasSaveBlockedReason => SaveBlockedReason is not null;

    private void OnLeasesChanged(object? sender, EventArgs e)
    {
        // Released from whichever thread finished the work, which is nearly never the UI one.
        _ = OnUiThreadAsync(() =>
        {
            SaveChangesCommand.NotifyCanExecuteChanged();
            SaveOnlyCommand.NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(CanImportHere));
            OnPropertyChanged(nameof(SaveBlockedReason));
            OnPropertyChanged(nameof(HasSaveBlockedReason));
        });
    }


    /// <summary>
    /// Re-reads the profile from the server and rebuilds the draft from it.
    /// </summary>
    /// <remarks>
    /// <b>This throws the draft away</b>, so it is only the three moments at which the server's list
    /// is the truth again: opening the page, discarding, and a save that has committed. Everything
    /// else that changes what is <em>known</em> - a source chip, a rescan, a folder added or removed,
    /// a drift notice's scan target - runs <see cref="RecomposeAsync"/> instead. The two used to be
    /// one method, which is why ticking a chip discarded the whole draft.
    /// </remarks>
    private async Task ReloadAsync()
    {
        IsLoading = true;

        try
        {
            // Asked together: neither depends on the other, and a profile with a long list is slow enough
            // to read without waiting for a second round trip behind it.
            var modListRead = _dependenciesClient.GetModDependenciesV1Async(
                _repo.Id, _profile.Id, null, _cancellation.Token);
            var ignoredRead = _profilesClient.GetProfileIgnoredModsV1Async(
                _repo.Id, _profile.Id, _cancellation.Token);

            var modList = await modListRead;
            var ignored = await ignoredRead;

            var snapshot = await _catalog.GetAsync(_cancellation.Token);

            // Everything from here down is WPF-facing, and this may well have arrived on a
            // thread-pool thread.
            await OnUiThreadAsync(() => Load(snapshot, modList, ignored));
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-reload.
        }
        finally
        {
            // Load clears this on the way through; the finally is for the paths that never reach it,
            // so a failed reload does not leave the list claiming to still be reading.
            IsLoading = false;
        }
    }

    /// <summary>
    /// Rebuilds what the catalog decides and nothing else: the source chips, the left list, the
    /// version selectors and the update plan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The draft survives it untouched</b>, and so do <see cref="_original"/>, the revision this
    /// page is based on, the search, both selections, the pending removals and the bulk undo. A
    /// change to the catalog is not a reason to re-read the profile.
    /// </para>
    /// <para>
    /// It is also the round trip that should never have been there: the catalog recomposes from
    /// scans already in memory, which is the whole reason it caches per source.
    /// </para>
    /// </remarks>
    private async Task RecomposeAsync()
    {
        // A save is written from a snapshot taken before it started, and rebuilding the lists under
        // it would replace the rows whose import is being reported into. The one that arrives is
        // held and run when the save is over - see SaveChanges.
        if (IsSaving)
        {
            _recomposeWhenSaved = true;

            return;
        }

        IsLoading = true;

        try
        {
            var snapshot = await _catalog.GetAsync(_cancellation.Token);

            await OnUiThreadAsync(() => Compose(snapshot));
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-scan.
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Load(ModCatalogSnapshot snapshot, GetModDependenciesResponse modList, ProfileIgnoredModsDto ignored)
    {
        _publishing = true;

        try
        {
            // Read from the response that carried the list, so the two cannot disagree about which
            // revision this page is editing.
            _basedOn = modList.Revision;

            foreach (var row in Pinned)
            {
                row.PropertyChanged -= OnPinnedRowChanged;
            }

            // Emptied before the index is built rather than after, so a version the outgoing draft
            // was the only holder of does not survive into the list it is being replaced by. The
            // versions a rejoined save contributed go with them: the server's list is the truth
            // again, and nothing in it is waiting to be uploaded.
            Pinned.Clear();

            _adopted = [];

            // Nothing that save reported is about the list that is being read now.
            _markedRun = null;

            RebuildSources(snapshot);
            BuildIndex(snapshot);

            foreach (var dependency in modList.Dependencies)
            {
                Pinned.Add(CreatePinnedRow(new ProfileModPin(
                    ModKey.From(dependency.ModId),
                    ModVersionKey.From(dependency.ModVersionId),
                    new ProfileModLock(false, dependency.Locked))));
            }

            _original = [.. Pinned.Select(x => x.Pin)];

            _originalAdded = ReadAdded(modList);
            _draftedAt = [];

            // The draft is the server's list again, so nothing is taken out of it. Written here
            // rather than left to the recount below, because the left list is built next and would
            // otherwise compose against the removals of the draft that has just been thrown away -
            // offering their versions, defaulting their rows to them, and giving a row to a mod that
            // is no longer removed at all.
            // The server's ignore list again, as the baseline and as the draft. Discarding reloads, so it
            // reverts what was ignored here along with the pins.
            _originalIgnored = [.. ignored.ModIds.Select(ModKey.From)];
            _ignoredMods = [.. _originalIgnored];

            RebuildAvailable();

            IsLoading = false;
        }
        finally
        {
            _publishing = false;
        }

        Recount();
    }

    /// <inheritdoc cref="RecomposeAsync"/>
    private void Compose(ModCatalogSnapshot snapshot)
    {
        _publishing = true;

        try
        {
            RebuildSources(snapshot);
            BuildIndex(snapshot);

            foreach (var row in Pinned)
            {
                row.Rebase(VersionsFor(row.ModId), _versionsByMod.GetValueOrDefault(row.ModId));
                row.RemoteOffer = _remoteOffers.GetValueOrDefault(row.ModId);
            }

            RebuildAvailable();

            IsLoading = false;
        }
        finally
        {
            _publishing = false;
        }

        Recount();
    }

    /// <summary>
    /// The chip row: the repo, then the folders, then the profiles - and what each of them
    /// contributes to the merged set.
    /// </summary>
    /// <remarks>
    /// The repo leads because it is the one that is on to begin with, and because everything after it
    /// is what it is being contrasted against. Each of the three is a genuine contributor to one
    /// union rather than a filter subtracting from the others, which is what makes "what does that
    /// profile have that this one does not" expressible at all: a profile's versions are registered,
    /// so under a subtractive repo chip they would vanish the moment it was switched off.
    /// </remarks>
    private void RebuildSources(ModCatalogSnapshot snapshot)
    {
        Sources.Clear();

        Sources.Add(new ModSourceViewModel(
            new ModSourceStatus(
                new ModSource(ModSourceId.Repo, _repo.Name, "Everything this repo has registered", ModSourceKind.Repo),
                _includeRegistered,
                snapshot.Versions.Count(x => x.IsOnServer),
                null),
            OnSourceEnabledChanged));

        foreach (var status in snapshot.Sources)
        {
            Sources.Add(new ModSourceViewModel(status, OnSourceEnabledChanged));
        }

        foreach (var profile in _profileSources)
        {
            Sources.Add(new ModSourceViewModel(
                new ModSourceStatus(profile.Source, profile.IsEnabled, profile.Pins.Count, null),
                OnSourceEnabledChanged));
        }

        // Last, because they are not part of the union the others make up: they put links on rows
        // rather than rows in the list.
        foreach (var remote in _remoteSources)
        {
            Sources.Add(CreateRemoteChip(remote));
        }

        HasEnabledFolders = snapshot.Sources.Any(x => x.IsEnabled);
        HasEnabledSources = _includeRegistered || HasEnabledFolders || _profileSources.Any(x => x.IsEnabled);

        _showSources = snapshot.Sources.Count(x => x.IsEnabled) > 1;

        // Once true, true forever - distinct from HasEnabledFolders, which is only about right now.
        // A folder switched off after finding nothing does not retroactively mean nothing was ever
        // checked, and the updates band's zero-state wording depends on telling the two apart.
        if (HasEnabledFolders && _hasReadAnyFolder is false)
        {
            _hasReadAnyFolder = true;

            OnPropertyChanged(nameof(UpdateCountText));
        }

        IndexOffered(snapshot);

        if (HasEnabledSources is false && PinnedFilter is PinnedModFilter.NotInSources)
        {
            // With nothing enabled the filter would select the whole profile and mean nothing, so the
            // chip is disabled - and a disabled chip must not be the one that is still checked.
            PinnedFilter = PinnedModFilter.All;
        }
    }

    /// <summary>
    /// What the enabled sources offer between them, which is what the left list is composed from.
    /// </summary>
    /// <remarks>
    /// A version reaches the catalog's merged set only from an enabled folder or from the repo, so
    /// the two flags on the record are the whole of what those two chips contribute. The profile
    /// chips are added here because the catalog never sees them.
    /// </remarks>
    private void IndexOffered(ModCatalogSnapshot snapshot)
    {
        var offered = new HashSet<ModVersionIdentity>();
        var mods = new HashSet<ModKey>();

        foreach (var version in snapshot.Versions)
        {
            if (version.IsLocal || (_includeRegistered && version.IsOnServer))
            {
                offered.Add(version.Identity);
                mods.Add(version.ModId);
            }
        }

        foreach (var profile in _profileSources.Where(x => x.IsEnabled))
        {
            foreach (var pin in profile.Pins)
            {
                offered.Add(new ModVersionIdentity(pin.ModId, pin.VersionId));
                mods.Add(pin.ModId);
            }
        }

        _offered = offered;
        _offeredMods = mods;
    }

    /// <summary>
    /// Every known version of every known mod, ordered - what the catalog has read, plus whatever the
    /// draft is pinning that the catalog does not know about.
    /// </summary>
    /// <inheritdoc cref="_versionsByMod" path="/remarks"/>
    private void BuildIndex(ModCatalogSnapshot snapshot)
    {
        // In this order because ModVersionIndex.Build keeps the first record of an identity it is
        // given: the catalog's is the freshest, the save's is next, and a pinned row's own copy only
        // fills the gap where neither has heard of it.
        _versionsByMod = ModVersionIndex.Build(
            snapshot.Known
                .Concat(_adopted)
                .Concat(Pinned.Select(x => x.SelectedVersion.Version)),
            _repo.Adapter.VersionComparer);

        // Here rather than beside it, because an offer is only an offer against what is known: a file
        // that has just been downloaded and scanned has to take its link away in the same pass.
        ComputeRemoteOffers();
    }

    /// <summary>
    /// The left list: one row per mod, with its own version selector offering what the chips offer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Offered, not known.</b> The selector's options are <see cref="_offered"/> - what the chips
    /// currently say counts - rather than everything <see cref="_versionsByMod"/> holds, which is the
    /// opposite rule from the right list's selector and deliberately so: symmetry would say don't
    /// narrow it, but a mod reached only through a profile chip has to show at that profile's version
    /// or switching the repo chip off would point the row at nothing enabled holds.
    /// </para>
    /// <para>
    /// <b>Except a pending removal</b>, whose taken-out version is offered regardless of the chips
    /// because it is draft state, not catalog state - and which is why the set of mods with a row is
    /// the offered ones <em>and</em> the removed ones. A mod no enabled chip holds any version of
    /// still has to have a row the moment this draft takes it out, or the header would count a
    /// removal nothing renders and the <em>Taken out</em> filter would select an empty list.
    /// </para>
    /// <para>
    /// <b>A chosen version survives the recompose.</b> A row already showing a mod keeps whatever its
    /// own selector had picked, provided that version is still offered - the same "the draft outlives
    /// the catalog" argument, one level down. A removal is the one exception: it always snaps back to
    /// the version that was taken out, which is what keeps the row's own + an undo rather than a
    /// different pin from the one that was there.
    /// </para>
    /// </remarks>
    private void RebuildAvailable()
    {
        var existing = new Dictionary<ModKey, ProfileModRowViewModel>();

        foreach (var row in _available)
        {
            row.PropertyChanged -= OnAvailableRowChanged;

            existing[row.ModId] = row;
        }

        var rows = new List<ProfileModRowViewModel>();

        foreach (var modId in _versionsByMod.Keys.Union(_pendingRemovals.Keys))
        {
            var set = _versionsByMod.GetValueOrDefault(modId);
            var (offered, defaultVersion) = OfferFor(modId);

            if (defaultVersion is null)
            {
                continue;
            }

            var row = existing.TryGetValue(modId, out var reused)
                ? ReuseAvailableRow(reused, offered, defaultVersion, IsPendingRemoval(modId), set)
                : new ProfileModRowViewModel(_repo.Id, offered, defaultVersion, false, _itemFactory, AllowWithoutAsking, set);

            row.Item.Sources = _showSources && row.SelectedVersion.Version.FoundIn.Count > 0
                ? string.Join(", ", row.SelectedVersion.Version.FoundIn.Select(source => source.Source.Name))
                : null;

            row.RemoteOffer = _remoteOffers.GetValueOrDefault(modId);

            // A row built while the profile is being saved has to come up inert like the rest of
            // them - OnIsReadOnlyChanged only reaches the rows that existed when the flag moved.
            row.Item.IsPickable = IsReadOnly is false;

            row.PropertyChanged += OnAvailableRowChanged;

            rows.Add(row);
        }

        _available = [.. rows.OrderBy(x => x.Name, NaturalOrder.Comparer)];

        // Rebuilt rather than refreshed, because the list behind it is replaced wholesale - adding a
        // couple of thousand rows to a bound collection one at a time is a couple of thousand layout
        // passes.
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(_available);
        view.Filter = x => x is ProfileModRowViewModel row && Passes(row);
        view.CustomSort = Comparer<ProfileModRowViewModel>.Create(CompareAvailable);

        AvailableView = view;
    }

    /// <summary>
    /// What one left-hand row offers and which of those it points at by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one place the two answers are worked out</b>, because a recompose and a bulk removal
    /// both need them and a row that disagreed with the list it is in about what it is offering is
    /// exactly the bug this replaced: the two used to be computed separately, and the second one
    /// appended the taken-out version to the newest end of the list whatever the ordering said.
    /// </para>
    /// <para>
    /// A null default means this mod has no row at all - nothing enabled offers a version of it and
    /// this draft has not taken it out.
    /// </para>
    /// </remarks>
    private (List<CatalogModVersion> Offered, CatalogModVersion? Default) OfferFor(ModKey modId)
    {
        var set = _versionsByMod.GetValueOrDefault(modId);

        var offered = set is null
            ? []
            : set.Order.Where(x => _offered.Contains(x.Identity)).ToList();

        return _pendingRemovals.TryGetValue(modId, out var removedPin)
            ? (offered, OfferRemoved(offered, set, removedPin))
            : (offered, offered.Count > 0 ? offered[^1] : null);
    }

    /// <summary>
    /// Puts the version a mod was taken out at into its row's selector, wherever the ordering says it
    /// belongs, and answers with it.
    /// </summary>
    /// <remarks>
    /// <b>Offered whatever the chips say</b>, because a pending removal is draft state rather than
    /// catalog state - without it the row's own <b>+</b> would re-add at the newest offered version,
    /// which is a different pin from the one that was there. Placed rather than appended: the
    /// selector reads newest first, so a taken-out version put on the end would read as the newest of
    /// the mod. A version the ordering has never heard of - a pin this client's catalog cannot
    /// resolve - goes to the front, which is where <see cref="CreatePinnedRow(ProfileModPin)"/> puts
    /// the same thing and for the same reason.
    /// </remarks>
    private static CatalogModVersion OfferRemoved(
        List<CatalogModVersion> offered,
        ModVersionSet? set,
        ProfileModPin removedPin)
    {
        if (offered.FirstOrDefault(x => x.VersionId == removedPin.VersionId) is CatalogModVersion already)
        {
            return already;
        }

        if (set?.Find(removedPin.VersionId) is not CatalogModVersion known)
        {
            var placeholder = Placeholder(removedPin.ModId, removedPin.VersionId);

            offered.Insert(0, placeholder);

            return placeholder;
        }

        // offered is a filter over set.Order and keeps its order, so counting the offered versions
        // the order puts before this one is the index it belongs at.
        var position = 0;

        foreach (var candidate in set.Order)
        {
            if (candidate.VersionId == removedPin.VersionId)
            {
                break;
            }

            if (offered.Contains(candidate))
            {
                position++;
            }
        }

        offered.Insert(position, known);

        return known;
    }

    /// <summary>A row that already exists for this mod, kept rather than rebuilt.</summary>
    private static ProfileModRowViewModel ReuseAvailableRow(
        ProfileModRowViewModel row,
        IReadOnlyList<CatalogModVersion> offered,
        CatalogModVersion defaultVersion,
        bool isRemoval,
        ModVersionSet? set)
    {
        var chosen = isRemoval is false
                && offered.FirstOrDefault(x => x.VersionId == row.SelectedVersion.Version.VersionId) is CatalogModVersion kept
            ? kept
            : defaultVersion;

        row.SetAvailableOptions(offered, chosen, set);

        return row;
    }

    /// <summary>
    /// The left list's confirm callback: nothing is committed by choosing what a row's own + would
    /// add, only by pressing it, so there is nothing here to ask about.
    /// </summary>
    private static Task<bool> AllowWithoutAsking(ProfileModRowViewModel row, ProfileModVersionOption option)
        => Task.FromResult(true);

    /// <summary>
    /// Forces a left row back to the version its mod was just taken out at. Called from
    /// <see cref="Recount"/>, which runs after every bulk move - a full
    /// <see cref="RebuildAvailable"/> is not, so this is what keeps a removal's row from lagging one
    /// recompose behind the removal itself.
    /// </summary>
    /// <remarks>
    /// Recomposed through <see cref="OfferFor"/> rather than worked out from what the row is holding,
    /// so a row snapped here and a row rebuilt by the next recompose cannot end up offering different
    /// things in a different order.
    /// </remarks>
    private void SnapToRemovedVersion(ProfileModRowViewModel row)
    {
        var (offered, defaultVersion) = OfferFor(row.ModId);

        if (defaultVersion is not null)
        {
            row.SetAvailableOptions(offered, defaultVersion, _versionsByMod.GetValueOrDefault(row.ModId));
        }
    }

    /// <summary>Every known version of one mod, oldest first. Empty for a mod nothing knows about.</summary>
    private IReadOnlyList<CatalogModVersion> VersionsFor(ModKey modId)
        => _versionsByMod.TryGetValue(modId, out var set) ? set.Order : [];

    /// <summary>
    /// The same, guaranteed to contain <paramref name="version"/> itself.
    /// </summary>
    /// <remarks>
    /// A row is built around one version and has to be able to show it, so a version the index has no
    /// record of joins the list rather than being dropped from it - at the front, where
    /// <see cref="CreatePinnedRow(ProfileModPin)"/> puts the same thing, so it cannot read as the
    /// newest. The one way in is a left-hand row standing for a taken-out mod whose pinned version
    /// this client's catalog cannot resolve: that version lives on the row and nowhere else, and
    /// pressing its <b>+</b> must put it back rather than leave the selector blank.
    /// </remarks>
    private IReadOnlyList<CatalogModVersion> VersionsFor(CatalogModVersion version)
    {
        var known = VersionsFor(version.ModId);

        return known.Any(x => x.VersionId == version.VersionId) ? known : [version, .. known];
    }

    /// <summary>
    /// Whether an enabled profile source is why this version is on the left, and whether that profile
    /// locks it.
    /// </summary>
    /// <remarks>
    /// <b>A profile chip carries the lock as well as the version.</b> Turning one on is asking for
    /// what that profile holds, and a row that brought the version but not the lock would disagree
    /// with <em>Copy from a profile…</em> - which already brings <c>Locked</c> across - about what
    /// "what that profile holds" means.
    /// </remarks>
    private bool LockedByProfileSource(ModVersionIdentity identity)
        => _profileSources.Any(x => x.IsEnabled && x.Locks(identity));

    /// <summary>The dispatcher hop every catalog read comes back through.</summary>
    private static Task OnUiThreadAsync(Action work)
        => Application.Current.Dispatcher.InvokeAsync(work).Task;

    /// <summary>
    /// A pinned row from a pin, tolerating a version this client's catalog has never heard of. Used
    /// by everything that builds the draft - the load, the undo, a restored removal and a copied
    /// list - so all four treat an unknown version the same way.
    /// </summary>
    private ProfileModRowViewModel CreatePinnedRow(ProfileModPin pin)
    {
        var versions = VersionsFor(pin.ModId);
        var selected = versions.FirstOrDefault(x => x.VersionId == pin.VersionId);

        if (selected is null)
        {
            // A fact about this client, not about the repo. It stays in the row's selector, at the
            // front so it cannot read as the newest, because a row that silently vanished would
            // leave the profile pinned to it with no way to say so.
            selected = Placeholder(pin.ModId, pin.VersionId);
            versions = [selected, .. versions];
        }

        return CreatePinnedRow(versions, selected, pin.Lock.ByProfile);
    }

    private ProfileModRowViewModel CreatePinnedRow(
        IReadOnlyList<CatalogModVersion> versions,
        CatalogModVersion selected,
        bool lockedByProfile)
    {
        var row = new ProfileModRowViewModel(
            _repo.Id,
            versions,
            selected,
            lockedByProfile,
            _itemFactory,
            ConfirmLockedVersionChangeAsync,
            _versionsByMod.GetValueOrDefault(selected.ModId),
            isPinned: true);

        row.RemoteOffer = _remoteOffers.GetValueOrDefault(selected.ModId);

        row.PropertyChanged += OnPinnedRowChanged;

        return row;
    }

    /// <summary>
    /// Stands in for a pinned version this catalog has no record of. Rendering it as a row keeps it
    /// removable, which a row that silently vanished would not be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not "deleted from the repo": a <c>ModDependency</c>'s foreign key onto <c>ModVersions</c> is
    /// required and <c>Restrict</c>, so a version a profile pins cannot be deleted at all. The
    /// reachable cause is a catalog that is behind the server - the registered half is cached until
    /// something invalidates it, while the dependencies are read fresh every load - so a teammate
    /// registering a version and pinning it lands here until this client next refetches.
    /// </para>
    /// <para>
    /// <c>IsOnServer</c> is therefore true, and load-bearing: the repo does hold this version, and a
    /// row claiming otherwise would report as pending and be handed to the importer at save, which
    /// has no file to import for it.
    /// </para>
    /// </remarks>
    private static CatalogModVersion Placeholder(ModKey modId, ModVersionKey versionId)
        => new(modId, versionId, modId.Value, string.Empty, IsLocal: false, IsOnServer: true, Locked: false);

    private ProfileModRowViewModel? FindPinned(ModKey modId)
        => Pinned.FirstOrDefault(x => x.ModId == modId);

    /// <summary>
    /// Everything the left list is showing.
    /// </summary>
    /// <remarks>
    /// <b>The hide rule is about versions, not mods.</b> It used to be "this mod is pinned", which
    /// put a newer version of a pinned mod nowhere at all - and a new version of a pinned mod is not
    /// in this profile, whatever else is. Which sources contributed the row is decided when the list
    /// is composed rather than here, because a chip changes what the list is <em>of</em> and a search
    /// only narrows what it then shows. <see cref="ShowRemovals"/> is the one thing that hides a row
    /// wholesale rather than narrowing it: defaulting to shown is what keeps a taken-out mod from
    /// being lost work nobody noticed leaving.
    /// </remarks>
    private bool Passes(ProfileModRowViewModel row)
        => PassesExceptFilter(row) && PassesFilter(row);

    /// <summary>
    /// Everything the left list is showing but for the filter chip. Split out for the one count that
    /// is a <em>link</em> to a filter rather than a count within one - it has to say how many rows
    /// clicking it will show, which is this and not <see cref="Passes"/>.
    /// </summary>
    private bool PassesExceptFilter(ProfileModRowViewModel row)
        => PassesExceptIgnore(row)
        && (ShowIgnored || IgnoreStateOf(row) is not (IgnoreState.Ignored or IgnoreState.OtherVersionOfLocked));

    /// <summary>
    /// Everything the left list is showing but for the ignore toggle, for the one count that says how
    /// many rows that toggle is holding back.
    /// </summary>
    private bool PassesExceptIgnore(ProfileModRowViewModel row)
        => row.Matches(SearchText)
        && IsPinnedAt(row.SelectedVersion.Version) is false
        && _downgraded.Contains(row.ModId) is false
        && (ShowRemovals || IsPendingRemoval(row) is false);

    /// <summary>
    /// Worked out from the draft when asked rather than stored on the row, because the filter runs
    /// while the list is being rebuilt and a stored answer would be one recount behind.
    /// </summary>
    private IgnoreState IgnoreStateOf(ProfileModRowViewModel row)
        => ProfileIgnoring.Classify(row.ModId, row.SelectedVersion.Version.VersionId, _ignoredMods, _pinnedState);

    /// <summary>
    /// Which pinned mods the user has explicitly moved backwards in this draft.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An available update shows in both lists; a chosen downgrade shows in one.</b> A mod pinned below its
    /// newest version is an update on the left, and that is right for a pin that merely fell behind. Once
    /// somebody has picked an older version on the right, offering the newer one again on the left is the
    /// list arguing with them, so the mod stays on the right alone.
    /// </para>
    /// <para>
    /// Measured against what the profile held when the page read it, so it lasts as long as the draft does:
    /// saved, the older pin is the profile's and the newer version is an update again - which is what a
    /// lock is for. Only where the order says so, like every other direction on this page: a version the
    /// comparer will not place is not a downgrade.
    /// </para>
    /// </remarks>
    private HashSet<ModKey> FindDowngraded()
    {
        var held = _original.ToDictionary(x => x.ModId, x => x.VersionId);
        var downgraded = new HashSet<ModKey>();

        foreach (var row in Pinned)
        {
            if (held.TryGetValue(row.ModId, out var was)
                && _versionsByMod.GetValueOrDefault(row.ModId)?.IsAfter(was, row.SelectedVersion.Version.VersionId) is true)
            {
                downgraded.Add(row.ModId);
            }
        }

        return downgraded;
    }

    /// <summary>Whether the profile pins this mod at exactly this version.</summary>
    private bool IsPinnedAt(CatalogModVersion version)
        => _pinnedVersions.TryGetValue(version.ModId, out var pinned) && pinned == version.VersionId;

    /// <summary>
    /// The left list's filter chip. Composes with the search rather than replacing it, which is what
    /// makes "everything shown" a single well-defined set for the bulk actions and the selection to
    /// be counted against.
    /// </summary>
    private bool PassesFilter(ProfileModRowViewModel row) => AvailableFilter switch
    {
        AvailableModFilter.New => row.Item.IsOnServer is false,
        AvailableModFilter.TakenOut => IsPendingRemoval(row),
        AvailableModFilter.Unordered => row.Item.OrderNotSettled,
        _ => true
    };

    /// <summary>Everything the right list is showing: the same search, and its own chip.</summary>
    private bool PassesPinned(ProfileModRowViewModel row)
        => row.Matches(SearchText)
        && PinnedFilter switch
        {
            PinnedModFilter.Updates => row.HasUpdate || row.RemoteOffer is not null,
            PinnedModFilter.Locked => row.IsLocked,
            PinnedModFilter.NotInSources => _offeredMods.Contains(row.ModId) is false,
            _ => true
        };

    /// <summary>
    /// The pinned rows as a plain list. <see cref="ObservableCollection{T}"/> is invariant, so the
    /// selection - which knows nothing about what kind of row it holds - cannot be handed it as it
    /// stands.
    /// </summary>
    private IReadOnlyList<ISelectableRow> PinnedRows() => [.. Pinned];

    /// <summary>
    /// The left list's order: what this draft has taken out of the profile, then what is an update to
    /// something it holds, then what could not be compared against the repo, then alphabetical.
    /// </summary>
    /// <remarks>
    /// A removed mod looks exactly like one that was never in the profile, and an update looks
    /// exactly like an addition - the only thing that can say otherwise is a chip and where the row
    /// sits. Read from the status the recount has just written rather than re-derived, so the sort
    /// and the chip cannot disagree. The ambiguous rank exists so the count beside the updates band
    /// is findable without opening its filter.
    /// </remarks>
    private int CompareAvailable(ProfileModRowViewModel left, ProfileModRowViewModel right)
    {
        var byRemoval = IsPendingRemoval(right).CompareTo(IsPendingRemoval(left));

        if (byRemoval != 0)
        {
            return byRemoval;
        }

        var byUpdate = IsUpdateRow(right).CompareTo(IsUpdateRow(left));

        if (byUpdate != 0)
        {
            return byUpdate;
        }

        var byAmbiguous = right.Item.OrderNotSettled.CompareTo(left.Item.OrderNotSettled);

        return byAmbiguous != 0
            ? byAmbiguous
            : NaturalOrder.Compare(left.Name, right.Name);
    }

    private static bool IsUpdateRow(ProfileModRowViewModel row) => row.Item.IsUpdateRow;

    private bool IsPendingRemoval(ProfileModRowViewModel row)
        => IsPendingRemoval(row.ModId);

    /// <inheritdoc cref="IsPendingRemoval(ProfileModRowViewModel)"/>
    private bool IsPendingRemoval(ModKey modId)
        => _pendingRemovals.ContainsKey(modId);

    /// <summary>
    /// What one left-hand row's status chip says: a fact about the version, worded for this profile.
    /// What the draft has done to the mod is not here - see <see cref="ProfileModTouch"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <b><em>Update</em></b> - a newer version of a mod this profile pins. <see cref="ModDisplayStatus.UpdateAvailable"/>
    /// where the repo holds it, so the move is free, and <see cref="ModDisplayStatus.UpdatePending"/> for
    /// a version only on disk, so saving imports it.
    /// </item>
    /// <item>
    /// <b><em>New version</em></b> - newer than anything the repo holds, of a mod this profile
    /// does not pin. An import candidate, which is what the repo mods page already calls an
    /// <em>Update</em> from its own point of view and which is not one from here: nothing in this
    /// profile moves by taking it.
    /// </item>
    /// <item><b><em>New</em></b> - a version the repo does not hold, with nothing else to say.</item>
    /// </list>
    /// A version the ordering will not place is none of the update states: the walk is the same
    /// abstention rule the update planner applies, and for the same reason.
    /// </remarks>
    private ModDisplayStatus DescribeRow(CatalogModVersion version)
    {
        // A mod the draft has taken out carries the Taken out mark instead - see ProfileModTouch - and
        // has no use for a second chip saying what is on offer of it.
        if (_pendingRemovals.ContainsKey(version.ModId))
        {
            return ModDisplayStatus.None;
        }

        var set = _versionsByMod.GetValueOrDefault(version.ModId);

        if (_pinnedVersions.TryGetValue(version.ModId, out var pinned))
        {
            if (set?.IsAfter(version.VersionId, pinned) is true)
            {
                return version.IsOnServer ? ModDisplayStatus.UpdateAvailable : ModDisplayStatus.UpdatePending;
            }

            return version.GetImportStatus();
        }

        if (version.IsOnServer)
        {
            return ModDisplayStatus.AlreadyInRepo;
        }

        return set?.NewestRegistered is CatalogModVersion newest && set.IsAfter(version.VersionId, newest.VersionId)
            ? ModDisplayStatus.NewVersion
            : ModDisplayStatus.New;
    }

    /// <summary>
    /// The right list's order: by name unless it has been switched to a date. What the draft has done to
    /// a mod is the row's own mark, and what a save is doing is the review view's to show, so nothing
    /// here needs to come first - and a list that keeps its order under the pointer is one somebody can
    /// edit, which is why the name sort is where the page opens.
    /// </summary>
    private int ComparePinned(ProfileModRowViewModel left, ProfileModRowViewModel right)
        => ProfileModSorting.Compare(PinnedSort, PinnedSortAscending, SortKey(left), SortKey(right));

    private static ProfileModSortKey SortKey(ProfileModRowViewModel row)
        => new(row.Name, row.Added);

    partial void OnPinnedSortChanged(ProfileModSort value)
    {
        // Set before the refresh below, so the list is not sorted by the new key in the old direction
        // first. Where the direction does not change this raises nothing, which is why the refresh is
        // asked for here as well.
        PinnedSortAscending = ProfileModSorting.DefaultAscending(value);

        ApplySortInfo(reorder: true);
    }

    partial void OnPinnedSortAscendingChanged(bool value)
    {
        ApplySortInfo(reorder: true);
    }

    /// <summary>
    /// Reverses the right list. Reading is not writing, so no <c>CanEdit</c> guard.
    /// </summary>
    [RelayCommand]
    private void TogglePinnedSortDirection() => PinnedSortAscending = PinnedSortAscending is false;

    /// <summary>
    /// When a row's mod arrived in the profile at the version the row now shows. The server's date where
    /// the row is still what the server holds, and otherwise the moment the draft put it there - so a
    /// mod the draft adds or moves is the newest thing in the list, and one it puts back where it was
    /// goes back to where it was.
    /// </summary>
    private static Dictionary<ModKey, (ModVersionKey Version, DateTime Added)> ReadAdded(GetModDependenciesResponse modList)
        => modList.Dependencies.ToDictionary(
            x => ModKey.From(x.ModId),
            x => (ModVersionKey.From(x.ModVersionId), x.Added));

    private DateTime AddedFor(ModKey mod, ModVersionKey version)
    {
        if (_originalAdded.TryGetValue(mod, out var saved) && saved.Version == version)
        {
            return saved.Added;
        }

        if (_draftedAt.TryGetValue(mod, out var drafted) is false || drafted.Version != version)
        {
            drafted = (version, DateTime.UtcNow);
            _draftedAt[mod] = drafted;
        }

        return drafted.At;
    }

    /// <summary>
    /// Writes each right-hand row's date and what it says about it, then re-sorts. Called
    /// from the recount as well as from a change of sort, because it is the draft that moves a row's date.
    /// </summary>
    /// <param name="reorder">Whether the list has to be re-sorted whatever moved - the sort itself changed.</param>
    private void ApplySortInfo(bool reorder = false)
    {
        _draftedAt = _draftedAt
            .Where(x => _pinnedIds.Contains(x.Key))
            .ToDictionary();

        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var row in Pinned)
        {
            var added = AddedFor(row.ModId, row.SelectedVersion.Version.VersionId);

            changed |= row.Added != added;

            row.Added = added;

            var key = SortKey(row);

            row.SortCaption = ProfileModSorting.Caption(PinnedSort, key, now);
            row.SortTooltip = ProfileModSorting.Describe(key);
        }

        if (reorder)
        {
            RefreshViews();
        }
        else if (changed && PinnedSort is not ProfileModSort.Name)
        {
            // Only the date sort can be moved by a date, and only a date that moved can move it. The
            // recount that called this counts the visible rows itself.
            PinnedView.Refresh();
        }
    }


    private void OnPinnedRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A bulk move writes many rows and recounts once at the end, so nothing here runs inside one.
        if (_publishing)
        {
            return;
        }

        // Picking a row changes nothing about the profile, so it recounts the selection and stops
        // there - a full recount would re-plan every update for a tick in a checkbox.
        if (e.PropertyName is nameof(ProfileModRowViewModel.IsSelected))
        {
            PinnedSelection.Recount();

            return;
        }

        // Only the two things a row can change about the profile. Everything else it raises - its
        // nested list row, the lock wording, the update marker this very method sets - either says
        // nothing about what is pinned or would recount from inside the recount that set it.
        if (e.PropertyName is nameof(ProfileModRowViewModel.SelectedVersion)
            or nameof(ProfileModRowViewModel.LockedByProfile))
        {
            Recount();
        }
    }

    /// <summary>
    /// What a row on the left can change by itself: its own checkbox, and now its own version
    /// selector - the row <em>is</em> the selected version, so a change to it moves the chip, the
    /// sort rank and the +/⬆ glyph together, exactly as it does on the right when <c>Item</c> is
    /// replaced.
    /// </summary>
    private void OnAvailableRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_publishing)
        {
            return;
        }

        if (e.PropertyName is nameof(ProfileModRowViewModel.IsSelected))
        {
            AvailableSelection.Recount();

            return;
        }

        if (e.PropertyName is nameof(ProfileModRowViewModel.SelectedVersion))
        {
            Recount();
        }
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(UpdateSelectedText));
        OnPropertyChanged(nameof(IgnoreSelectedText));
        OnPropertyChanged(nameof(UnignoreSelectedText));
        IgnoreSelectedCommand.NotifyCanExecuteChanged();
        UnignoreSelectedCommand.NotifyCanExecuteChanged();
        AddSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        LockSelectedCommand.NotifyCanExecuteChanged();
        UnlockSelectedCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Freezes or releases the one control the shared row template owns: its checkbox.
    /// </summary>
    /// <remarks>
    /// Written per row rather than by disabling the list, because the list has to stay scrollable
    /// and the mod name has to stay clickable - see <see cref="IsReadOnly"/>. Everything else the
    /// page owns is bound to <see cref="CanEdit"/> where it is declared.
    /// </remarks>
    partial void OnIsReadOnlyChanged(bool value)
    {
        foreach (var row in _available)
        {
            row.Item.IsPickable = value is false;
        }

        foreach (var row in Pinned)
        {
            row.Item.IsPickable = value is false;
        }
    }

    partial void OnAvailableFilterChanged(AvailableModFilter value)
    {
        if (value is AvailableModFilter.TakenOut)
        {
            // A filter that selects a set the toggle above is hiding is an empty list with no
            // explanation, so ticking it forces the toggle back on.
            ShowRemovals = true;
        }

        RefreshViews();
    }

    partial void OnShowRemovalsChanged(bool value)
    {
        RefreshViews();
    }

    partial void OnShowIgnoredChanged(bool value)
    {
        RefreshViews();
    }

    partial void OnPinnedFilterChanged(PinnedModFilter value)
    {
        RefreshViews();
    }

    private void OnPinnedChanged()
    {
        if (_publishing)
        {
            return;
        }

        Recount();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshViews();
    }

    /// <summary>
    /// What both lists do when the set they are showing changes without the draft changing - a
    /// search, or a filter chip. The selections are recounted rather than touched: a row that has
    /// scrolled out of the view is still picked, and how many of those there are is exactly what the
    /// bar above each list has to be able to say.
    /// </summary>
    private void RefreshViews()
    {
        // A rebuild sets the filter chips back where they are no longer offered, and refreshing a
        // view over a list that is halfway through being replaced is work thrown away - every
        // publishing block ends in a recount, which refreshes both views itself.
        if (_publishing)
        {
            return;
        }

        AvailableView?.Refresh();
        PinnedView.Refresh();

        RecountAvailable();
        RecountPinnedVisible();

        AvailableSelection.Recount();
        PinnedSelection.Recount();
    }

    /// <summary>
    /// What the left list is showing, and how much of that the profile has never held - the two the
    /// bulk buttons are counted against, and they part company as soon as something is taken out.
    /// </summary>
    /// <remarks>
    /// The total counts what the <em>sources</em> hold and the search does not: "42 of 900" is how
    /// much a search is hiding, and switching a source off is not a search - it changes what the list
    /// is of, which is why the list itself is rebuilt rather than filtered when a chip moves.
    /// </remarks>
    private void RecountAvailable()
    {
        // Ignored rows are not part of the total until they are being shown: hidden by a toggle is not
        // hidden by a search, and "412 of 900" would say the search had found 488 fewer than it had.
        AvailableTotal = _available.Count(x => IsPinnedAt(x.SelectedVersion.Version) is false
            && (ShowIgnored || IgnoreStateOf(x) is not (IgnoreState.Ignored or IgnoreState.OtherVersionOfLocked)));

        IgnoredCount = _available.Count(x => PassesExceptIgnore(x)
            && PassesFilter(x)
            && IgnoreStateOf(x) is IgnoreState.Ignored or IgnoreState.OtherVersionOfLocked);

        AvailableCount = _available.Count(Passes);
        NewCount = _available.Count(IsShownAndNew);

        // Counted against every test the list applies except the filter chip this count is a link
        // to, so clicking it shows exactly this many rows. Counting them against the raw row set
        // instead was the drift: a row whose selected version is what the profile pins is hidden by
        // IsPinnedAt, which is the ordinary state of a mod pinned at the newest version there is, so
        // the band offered three and the filter showed one.
        AmbiguousCount = _available.Count(x => x.Item.OrderNotSettled && PassesExceptFilter(x));
    }

    /// <summary>How many of the profile's mods the search is showing. The total is PinnedCount.</summary>
    private void RecountPinnedVisible()
    {
        PinnedVisibleCount = Pinned.Count(PassesPinned);
    }

    /// <summary>
    /// Re-derives everything the draft decides, and is the only place any of it is written.
    /// </summary>
    /// <remarks>
    /// <b>Not re-entrant, deliberately.</b> Snapping a removal's row to its taken-out version moves
    /// that row's selector, which the row reports and this method listens for - so without the guard
    /// each snapped row started a nested recount that snapped the next one, recursing once per
    /// removal and re-planning every update on the way down. The nested call has nothing to add
    /// either way: what triggered it is a change this pass made, and the rest of this pass reads the
    /// result of it.
    /// </remarks>
    private void Recount()
    {
        if (_recounting)
        {
            return;
        }

        _recounting = true;

        try
        {
            RecountCore();
        }
        finally
        {
            _recounting = false;
        }
    }

    private void RecountCore()
    {
        // The bulk move that armed the offer sets it again once this has run - see RunBulk.
        RetireUndo();

        _pinnedIds = [.. Pinned.Select(x => x.ModId)];
        _pinnedVersions = Pinned.ToDictionary(x => x.ModId, x => x.SelectedVersion.Version.VersionId);
        _pinnedState = Pinned.ToDictionary(x => x.ModId, x => (x.SelectedVersion.Version.VersionId, x.IsLocked));
        _downgraded = FindDowngraded();
        _pendingRemovals = _original
            .Where(x => _pinnedIds.Contains(x.ModId) is false)
            .ToDictionary(x => x.ModId);

        // A mod no enabled chip offers any version of has no row on the left at all, so a draft that
        // takes one out would count a removal nothing renders and offer a Taken out filter that
        // selects an empty list. Building a row for it needs the whole list rebuilt, which is why
        // this asks first: the ordinary bulk removal touches mods that already have rows and still
        // costs nothing but the snap below.
        var shown = _available.Select(x => x.ModId).ToHashSet();

        if (_pendingRemovals.Keys.Any(x => shown.Contains(x) is false))
        {
            RebuildAvailable();
        }

        // A pending removal always shows the version that was taken out, whatever this row was
        // showing a moment ago - a bulk removal does not run RebuildAvailable, so without this the
        // row's own + could re-add at the newest offered instead of undoing the removal. Recomputed
        // here rather than only at the next recompose, because the hazard this exists to route
        // around is exactly a click that happens before one.
        foreach (var row in _available)
        {
            if (_pendingRemovals.TryGetValue(row.ModId, out var removedPin)
                && row.SelectedVersion.Version.VersionId != removedPin.VersionId)
            {
                SnapToRemovedVersion(row);
            }
        }

        // The chip that says what a row is. Marked here rather than when the row is built, because
        // it is the draft that decides it and the draft changes under the same rows.
        foreach (var row in _available)
        {
            row.Item.Status = DescribeRow(row.SelectedVersion.Version);
            row.Item.OrderNotSettled = _versionsByMod.GetValueOrDefault(row.ModId)?.CouldNotCompareToNewest(row.SelectedVersion.Version.VersionId) ?? false;

            // What pressing this row's button does, which is a question about the draft rather than
            // about the version: any version of a mod the profile already holds moves its pin, newer
            // or not. Written beside the status, because Item is replaced whenever the selector moves
            // and both of these belong to whichever version it landed on.
            row.Item.MovesPin = _pinnedIds.Contains(row.ModId);

            // What the row's eye offers, for the same reason: it is the draft that says whether the mod
            // is pinned or locked.
            row.IgnoreState = IgnoreStateOf(row);

            // A row on this side is only ever touched by having been taken out. Everything else the
            // draft can do to a mod happens to a row on the other side.
            _pendingRemovals.TryGetValue(row.ModId, out var removed);

            row.Touch = ProfileModTouches.Classify(removed, null);
            row.TouchTooltip = ProfileModTouches.Describe(removed, null);
        }

        var savedPins = _original.ToDictionary(x => x.ModId);

        foreach (var row in Pinned)
        {
            savedPins.TryGetValue(row.ModId, out var saved);

            row.Touch = ProfileModTouches.Classify(saved, row.Pin);
            row.TouchTooltip = ProfileModTouches.Describe(saved, row.Pin);
        }

        ApplySortInfo();

        PinnedCount = Pinned.Count;
        RecountPinnedVisible();
        RemovalCount = _pendingRemovals.Count;
        PendingCount = Pinned.Count(x => x.IsPending);

        // The right list's own filter can depend on facts that are not about the pinned rows at all -
        // "Not in sources" reads _offeredMods, which a rescan or a source chip changes without a
        // single row being added to or removed from Pinned, and a CollectionView does not re-run its
        // filter over unchanged items on its own. Without this the count above updated - it reads
        // PassesPinned directly - but the list on screen did not, until some other change flipped the
        // filter chip and forced a refresh as a side effect.
        PinnedView.Refresh();

        // The left list hides what the right one holds, so it re-filters whenever that changes -
        // which is also what keeps a mod off both sides at once.
        AvailableView?.Refresh();
        RecountAvailable();

        _updates = ProfileModUpdates.Plan(Pinned.Select(x => x.Pin), _versionsByMod);

        var byMod = _updates.Available.Concat(_updates.Skipped).ToDictionary(x => x.ModId);

        foreach (var row in Pinned)
        {
            var update = byMod.GetValueOrDefault(row.ModId);

            row.UpdateTo = update?.To;
            row.UpdateImportsOnSave = update?.ImportsOnSave ?? false;
        }

        UpdateCount = _updates.Count;
        ApplicableUpdateCount = _updates.Available.Count;
        SkippedUpdateCount = _updates.Skipped.Count;
        PendingUpdateCount = _updates.PendingCount;
        FreeUpdateCount = _updates.FreeCount;

        RecountRemoteUpdates();

        // Last, because both of them count against the views this method has just re-filtered.
        AvailableSelection.Recount();
        PinnedSelection.Recount();

        RefreshUnsaved();

        // Read from the draft, so it is redrawn from here and from nowhere else. With nothing left to
        // review - the last change taken back, a save that has committed, a discard - there is nothing
        // to show, and the lists are where the page goes.
        if (IsReviewing)
        {
            if (ChangeCount == 0)
            {
                IsReviewing = false;
            }
            else
            {
                RebuildChanges();
            }
        }
    }

    private static string DescribeChanges(ProfileModListChanges diff)
    {
        var parts = new[]
        {
            diff.Added.Count > 0 ? $"{diff.Added.Count} added" : null,
            diff.Changed.Count > 0 ? $"{diff.Changed.Count} changed" : null,
            diff.Removed.Count > 0 ? $"{diff.Removed.Count} taken out" : null
        };

        return string.Join(" · ", parts.OfType<string>());
    }

    /// <summary>
    /// Whether the draft differs from what the server holds, in either of the two things a save writes.
    /// </summary>
    /// <remarks>
    /// Kept apart because they are saved apart, and cost differently: a changed mod list is a revision,
    /// an import and a re-apply, while a changed ignore list is one small write that touches nothing on
    /// disk. See <see cref="HasModListChanges"/>.
    /// </remarks>
    private void RefreshUnsaved()
    {
        var diff = ProfileModListDiff.Compute(_original, Pinned.Select(x => x.Pin));

        HasModListChanges = diff.IsEmpty is false;
        ChangeCount = diff.Count;
        ChangeSummary = DescribeChanges(diff);
        HasIgnoredChanges = DesiredIgnored().SetEquals(_originalIgnored) is false;
        HasUnsavedChanges = HasModListChanges || HasIgnoredChanges;

        if (HasUnsavedChanges)
        {
            _navigationLock.AcquireLock(this);
        }
        else
        {
            _navigationLock.ReleaseLock(this);
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        /// <param name="scanTarget">
        /// A folder that should be scanned from the start. Null for an ordinary
        /// navigation, which reads no disk at all until the user ticks a source.
        /// </param>
        public ProfileModsEditorPageViewModel Create(Repo repo, ProfileDto profile, ModTargetRef? scanTarget = null)
        {
            var page = ActivatorUtilities.CreateInstance<ProfileModsEditorPageViewModel>(serviceProvider, repo, profile);

            // Set before the page is handed back, so it is on by the time TriggerInit reads it. Done
            // here rather than through the constructor because a nullable Guid does not survive
            // ActivatorUtilities' positional matching.
            if (scanTarget is ModTargetRef target)
            {
                page.ScanTarget(target);
            }

            return page;
        }
    }
}

/// <summary>
/// What the left list is narrowed to, on top of the search.
/// </summary>
/// <remarks>
/// Deliberately few, and each of them a question somebody actually arrives with. A filter here is
/// not only a way of finding one mod - the search does that - it is a way of naming a <em>set</em>
/// for the bulk actions and the selection to be applied to, so a filter that nobody would want to
/// act on all of is a filter that earns nothing.
/// </remarks>
public enum AvailableModFilter
{
    All,

    /// <summary>On this computer but not in the repo: what a save would have to import.</summary>
    New,

    /// <summary>Back on this side because this draft took it out of the profile.</summary>
    TakenOut,

    /// <summary>
    /// The ordering could not compare this version against what the repo holds - neither before,
    /// after, nor equal. Took the slot <c>Conflicts</c> used to have: a source conflict is answered
    /// at save, one version at a time, in a dialog, so a filter for it never earned its place - the
    /// row's own chip is the whole of what it needs. This one is answered the same way, but is worth
    /// isolating because it is silent otherwise: no update, no chip, no count, nothing to say why a
    /// version somebody might have come here for was never offered as one.
    /// </summary>
    Unordered
}

/// <inheritdoc cref="AvailableModFilter"/>
public enum PinnedModFilter
{
    All,

    /// <summary>
    /// A newer version exists, whether or not the repo holds it, whether or not the pin is free to move -
    /// and whether it is here at all or only on a remote source such as ModHub.
    /// </summary>
    Updates,

    Locked,

    /// <summary>
    /// The mods this profile pins that no enabled source offers any version of.
    /// </summary>
    /// <remarks>
    /// <b>The mirror of the left list's diff view, and the half of it that was missing.</b> With
    /// another profile as the only enabled source this lists exactly what this profile holds and that
    /// one does not, so with <em>Take out everything shown</em> under it, "make this profile match
    /// that one" is two clicks. Mod-level rather than version-level, deliberately: a mod the other
    /// profile holds at a different version is an update, not a removal, and the left list already
    /// says so.
    /// </remarks>
    NotInSources
}


/// <summary>
/// Another profile in this repo, read as a source.
/// </summary>
/// <remarks>
/// A membership set and a version per mod rather than a scan - see
/// <see cref="ModSourceKind.Profile"/>. The lock travels with the version, which is what makes
/// turning a chip on agree with <em>Copy from a profile…</em> about what "what that profile holds"
/// means.
/// </remarks>
internal sealed class ProfileModSource
{
    private readonly HashSet<ModVersionIdentity> _locked;


    public ProfileModSource(Guid profileId, ModSource source, IReadOnlyList<ProfileModPin> pins)
    {
        ProfileId = profileId;
        Source = source;
        Pins = pins;

        _locked = [.. pins
            .Where(x => x.Lock.ByProfile)
            .Select(x => new ModVersionIdentity(x.ModId, x.VersionId))];
    }


    public Guid ProfileId { get; }
    public ModSource Source { get; }
    public IReadOnlyList<ProfileModPin> Pins { get; }

    /// <summary>Whether this chip is being read. Switched off rather than removed, like a folder.</summary>
    public bool IsEnabled { get; set; } = true;

    public bool Locks(ModVersionIdentity identity) => _locked.Contains(identity);
}

/// <summary>
/// A remote source - ModHub - as the editor holds it: whether its chip is on, what it has been asked,
/// and what it answered.
/// </summary>
/// <remarks>
/// Answers are kept by mod across chip toggles and recomposes, so switching a folder on asks only about
/// the mods it brought and switching the chip off and on again asks nothing at all.
/// </remarks>
internal sealed class RemoteModSourceState(IRemoteModSource remote)
{
    public IRemoteModSource Remote { get; } = remote;
    public ModSourceId Id { get; } = ModSourceId.ForRemote(remote.Key);

    /// <summary>The chip's source, with what the source currently says as its tooltip.</summary>
    public ModSource Source => new(Id, Remote.DisplayName, Describe(), ModSourceKind.Remote);

    /// <summary>
    /// <b>On from the start</b>, unlike every other source but the repo. The rule that sources start off
    /// is about a page never reading a disk just because somebody navigated to it; this reads the ModsDude
    /// server, which the page is reading anyway to load the profile, and a chip nobody knows to click is
    /// updates nobody sees.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public bool IsLookingUp { get; set; }
    public string? Error { get; set; }

    /// <summary>Every mod this source has been asked about, answered or not.</summary>
    public HashSet<ModKey> Asked { get; } = [];

    /// <summary>What the source said it has, by mod - at whatever version, newer or not.</summary>
    public Dictionary<ModKey, RemoteModOffer> Answers { get; } = [];

    public bool HasAnswered { get; set; }
    public DateTimeOffset? CurrentAsOf { get; set; }

    /// <summary>How many rows carry a link from this source, which is what its chip counts.</summary>
    public int OfferCount { get; set; }

    /// <summary>
    /// Whether the source answered without being able to vouch for the answer - the server still reading
    /// ModHub for the first time - so it may be missing most of what the source has.
    /// </summary>
    public bool IsIncomplete => HasAnswered && CurrentAsOf is null;


    public void Forget()
    {
        Asked.Clear();
        Answers.Clear();
        HasAnswered = false;
        CurrentAsOf = null;
        Error = null;
    }


    private string Describe()
    {
        var name = Remote.DisplayName;
        var what = $"Newer versions {name} has of the mods here, as a link on each mod's row.";

        if (IsEnabled is false)
        {
            return $"{what}\nSwitched off: no links are shown.";
        }

        if (HasAnswered is false && IsLookingUp is false)
        {
            return what;
        }

        if (HasAnswered is false)
        {
            return $"{what}\nAsking the ModsDude server…";
        }

        return CurrentAsOf is DateTimeOffset asOf
            ? $"{what}\nAs of {asOf.LocalDateTime:g}."
            : $"{what}\nThe server is still reading {name} for the first time, so this is missing most of what it has. Switch this off and on again later to ask again.";
    }
}
