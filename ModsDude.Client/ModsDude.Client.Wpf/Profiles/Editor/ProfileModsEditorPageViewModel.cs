using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Profiles.Editor;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Mods;
using ModsDude.Client.Wpf.Shared.Behaviors;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Notices;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>
/// The profile's mod list: what the enabled sources offer on the left, the draft on the right.
/// </summary>
/// <remarks>
/// <para>
/// <b>One path for every change.</b> The draft and the loaded sources are one immutable
/// <see cref="ProfileEditorSnapshot"/> in an undo history. Every edit produces a new snapshot,
/// <see cref="ProfileEditorState.Compute"/> turns it into both lists and every count, and the rows are
/// updated in place from that. Nothing on the page is kept in sync by hand.
/// </para>
/// <para>
/// <b>Nothing is uploaded until Save.</b> A pin to a version the repo does not hold is imported by the
/// save, and a save whose import does not fully succeed writes nothing.
/// </para>
/// <para>
/// <b>Kept alive while it holds work.</b> Leaving with unsaved changes or a save running keeps the page
/// hidden instead of asking, so the draft, its sources and a failed save's marks are all there on
/// return. It is dropped once the work is done, or can no longer be saved.
/// </para>
/// </remarks>
public partial class ProfileModsEditorPageViewModel : PageViewModel, IRetainablePage
{
    private readonly Repo _repo;
    private readonly Profile _profile;
    private readonly ModCatalog _catalog;
    private readonly ModListItemViewModel.Factory _itemFactory;
    private readonly IProfileSaveService _saveService;
    private readonly IProfileModPinsClient _pinsClient;
    private readonly IProfilesClient _profilesClient;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly IProfileStore _profileStore;
    private readonly IRepoStore _repoStore;
    private readonly IGameRepository _gameRepository;
    private readonly IProfileApplyService _applyService;
    private readonly IModSyncService _syncService;
    private readonly IDriftMonitor _driftMonitor;
    private readonly NoticeCenterViewModel _notices;
    private readonly IResourceLeases _leases;
    private readonly IToastService _toasts;
    private readonly TimeProvider _time;
    private readonly IRemoteDownloadNotes _downloadNotes;
    private readonly ActiveProfile _activeProfile;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<Game> _watchedGames = [];
    private readonly IReadOnlyList<IRelayCommand> _commands;

    private readonly EditHistory<ProfileEditorSnapshot> _history = new(new(new ProfileDraft([], [], []), []));
    private readonly RequestIdKeeper<SaveAttempt> _saveRequestIds = new();
    private readonly OtherProfilesReader _otherProfiles;
    private readonly Dictionary<ModKey, ModVersionKey> _availableChoices = [];

    private readonly Dictionary<ModKey, AvailableModRowViewModel> _availableRows = [];
    private readonly Dictionary<ModKey, PinnedModRowViewModel> _pinnedRows = [];

    /// <summary>The catalog as last read, before the remote sources' answers are placed on it.</summary>
    private ProfileEditorCatalog _catalogRead;

    /// <summary><see cref="_catalogRead"/> with the remote sources' answers placed on it. What the page shows.</summary>
    private ProfileEditorCatalog _catalogView;
    private ProfileEditorState _state;
    private IReadOnlyDictionary<ModKey, SavedPinDate> _savedDates = new Dictionary<ModKey, SavedPinDate>();
    private ModSearchQuery _searchQuery = ModSearchQuery.Empty;
    private int _basedOn;
    private int _ignoredVersion;
    private int _recomposeGeneration;
    private bool _skipApplyOnce;
    private ProfileSaveRun? _markedRun;
    private IToast? _activationToast;
    private bool _suspended;


    public ProfileModsEditorPageViewModel(
        Repo repo,
        Profile profile,
        ModCatalog.Factory catalogFactory,
        ModListItemViewModel.Factory itemFactory,
        IProfileSaveService saveService,
        IProfileModPinsClient pinsClient,
        IProfilesClient profilesClient,
        IProfileStore profileStore,
        IRepoStore repoStore,
        IModalService modalService,
        IErrorReporter errorReporter,
        IFilePickerService filePickerService,
        IGameRepository gameRepository,
        IProfileApplyService applyService,
        IModSyncService syncService,
        IDriftMonitor driftMonitor,
        NoticeCenterViewModel notices,
        IResourceLeases leases,
        IToastService toasts,
        IRemoteDownloadNotes downloadNotes,
        TimeProvider time)
    {
        _repo = repo;
        _profile = profile;
        _itemFactory = itemFactory;
        _saveService = saveService;
        _pinsClient = pinsClient;
        _profilesClient = profilesClient;
        _modalService = modalService;
        _errorReporter = errorReporter;
        _profileStore = profileStore;
        _repoStore = repoStore;
        _gameRepository = gameRepository;
        _applyService = applyService;
        _syncService = syncService;
        _driftMonitor = driftMonitor;
        _notices = notices;
        _leases = leases;
        _toasts = toasts;
        _time = time;
        _downloadNotes = downloadNotes;
        _activeProfile = new ActiveProfile(repo.Id, profile.Id);

        _catalog = catalogFactory.Create(repo);
        _catalogRead = ProfileEditorCatalog.Empty(repo.Adapter.VersionComparer);
        _catalogView = _catalogRead;
        _otherProfiles = new OtherProfilesReader(profileStore, pinsClient, repo.Id, profile.Id);

        Sources = new EditorSourcesViewModel(
            _catalog,
            repo.Adapter.GetBaseCapabilityAdapterFactory<IRemoteModSourcesAdapter>()?.Invoke().Sources ?? [],
            _otherProfiles, filePickerService, modalService, errorReporter,
            () => _history.Current, CommitAsync, RecomposeAsync, _cancellation.Token);

        Sources.RemoteChanged += ShowRemote;

        _history.Reset(Current with { Loaded = Sources.Targets });

        _state = ProfileEditorState.Compute(Inputs());
        _summary = Summarize();

        SearchCompleter = new ModSearchCompleter(_catalog.Attributes);
        SortAttributes = [.. _catalog.Attributes.Select(x => x.Key)];

        AvailableSorting.Changed += Refresh;
        PinnedSorting.Changed += Refresh;

        AvailableSelection = new ModListSelection(() => AvailableRows, () => [.. _availableRows.Values], AddRows, "Add");
        PinnedSelection = new ModListSelection(() => PinnedRows, () => [.. _pinnedRows.Values], RemoveRows, "Remove");
        AvailableSelection.Changed += OnSelectionChanged;
        PinnedSelection.Changed += OnSelectionChanged;

        _commands = [.. GetType().GetProperties()
            .Where(x => typeof(IRelayCommand).IsAssignableFrom(x.PropertyType))
            .Select(x => (IRelayCommand)x.GetValue(this)!)];

        _syncService.ModFolderChanged += OnModFolderChanged;
        _repo.Games.CollectionChanged += OnGamesChanged;
        _leases.Changed += OnLeasesChanged;
        _profile.PropertyChanged += OnProfileChanged;
        _repo.PropertyChanged += OnRepoChanged;
        _repoStore.Repos.CollectionChanged += OnReposChanged;
        _profileStore.Changed += OnProfilesChanged;

        // Somebody already looking at the drifted profile's mod list does not need to be told about it.
        _notices.SuppressFor(_activeProfile);

        RefreshApplyTargets();
    }


    public string ProfileName => _profile.Name;

    public EditorSourcesViewModel Sources { get; }

    public BulkObservableCollection<AvailableModRowViewModel> AvailableRows { get; } = [];
    public BulkObservableCollection<PinnedModRowViewModel> PinnedRows { get; } = [];

    public ModListSelection AvailableSelection { get; }
    public ModListSelection PinnedSelection { get; }

    public ListSortViewModel AvailableSorting { get; } =
        new(ModListSortKind.Name, "Date imported", "Versions not in the repo count as newest");

    public ListSortViewModel PinnedSorting { get; } =
        new(ModListSortKind.Name, "Date added", "Unsaved changes count as newest");

    /// <summary>The attribute keys either list can be sorted by, in the adapter's order.</summary>
    public IReadOnlyList<string> SortAttributes { get; }

    public ModSearchCompleter SearchCompleter { get; }

    [ObservableProperty]
    private ProfileModsEditorSummary _summary;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AvailableModFilter _availableFilter;

    [ObservableProperty]
    private bool _showIgnored;

    [ObservableProperty]
    private PinnedModFilter _pinnedFilter;

    /// <summary>Whether the catalog is being read. The draft may still be edited meanwhile.</summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>
    /// What the page is doing with the draft. Only <see cref="EditorPhase.Editing"/> lets anything change
    /// it; the lists stay scrollable and readable, and every control that writes binds to <see cref="CanEdit"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(IsWaitingForSave))]
    [NotifyPropertyChangedFor(nameof(HasLoadFailed))]
    private EditorPhase _phase = EditorPhase.Loading;

    public bool CanEdit => Phase is EditorPhase.Editing;

    /// <summary>A save of this profile that this page did not start is running; the page waits for it.</summary>
    public bool IsWaitingForSave => Phase is EditorPhase.WaitingForSave;

    /// <summary>The profile could not be read, so there is no draft to edit until it is read again.</summary>
    public bool HasLoadFailed => Phase is EditorPhase.LoadFailed;

    /// <summary>What to call this save in the profile's history. Optional.</summary>
    [ObservableProperty]
    private string _versionDescription = "";

    [ObservableProperty]
    private Game? _applyTarget;

    public bool HasUnsavedChanges => _history.Current.Draft.HasChanges;

    public object RetentionKey => new ProfileModsEditorKey(_repo.Id, _profile.Id);

    public bool IsWorthRetaining => CanStillSave && ((CanEdit && HasUnsavedChanges) || Phase is EditorPhase.Saving);

    public string RetentionDescription => $"Unsaved changes to '{_profile.Name}'";

    /// <summary>Whether this user can still save this profile's mod list at all.</summary>
    private bool CanStillSave
        => _repo.MembershipLevel >= RepoMembershipLevel.Member
        && _repoStore.Repos.Contains(_repo)
        && _profileStore.Find(_repo.Id, _profile.Id) is not null;

    public string UndoTooltip => _history.UndoDescription is string what ? $"Undo: {what} (Ctrl+Z)" : "Nothing to undo";
    public string RedoTooltip => _history.RedoDescription is string what ? $"Redo: {what} (Ctrl+Y)" : "Nothing to redo";

    public string SaveOnlyDescription =>
        "Installed mods are not changed";

    public string UpdateRepoOnlyDescription =>
        "Only versions already in the repo";

    public string IgnoreSelectedText => Counted("Ignore", PickedAvailable().Count(x => x.IsIgnored is false));
    public string UnignoreSelectedText => Counted("Stop ignoring", PickedAvailable().Count(x => x.IsIgnored));
    public string DismissSelectedVersionsText => Counted("Dismiss", VersionsToDismiss().Count);
    public string DismissSelectedUpdatesText => Counted("Dismiss", UpdatesToDismiss().Count);
    public string RevertSelectedText => Counted("Revert", PickedPinned().Count(x => x.IsChanged));

    public string UpdateSelectedText
    {
        get
        {
            var skipped = PickedPinned().Count(x => x.HasUpdate && x.IsLocked);

            return skipped > 0 ? $"Update (skip {skipped} locked)" : "Update";
        }
    }

    /// <summary>Whether a save could import what the draft holds, which another import into this repo can block.</summary>
    public bool CanImportHere => _state.PendingCount == 0 || _saveService.IsImportBusy(_repo.Id) is false;

    /// <summary>Only while editing: during this page's own save, the import holding the repo is the save's.</summary>
    public string? SaveBlockedReason
        => CanEdit && CanImportHere is false ? "Another import is running in this repo" : null;

    public bool HasSaveBlockedReason => SaveBlockedReason is not null;


    private ProfileEditorSnapshot Current => _history.Current;

    private ProfileDraft Draft => _history.Current.Draft;


    #region The one path

    private ProfileEditorInputs Inputs() => new(_history.Current.Draft, _catalogView, _time.GetLocalNow())
    {
        IncludeRegistered = Sources.IncludeRegistered,
        ProfileSources = Sources.EnabledProfiles,
        AvailableChoices = _availableChoices,
        SavedDates = _savedDates,
        Search = _searchQuery,
        AvailableFilter = AvailableFilter,
        ShowIgnored = ShowIgnored,
        AvailableSort = AvailableSorting.Sort,
        PinnedFilter = PinnedFilter,
        PinnedSort = PinnedSorting.Sort
    };

    /// <summary>Recomputes everything from the current snapshot and view, and shows it.</summary>
    private void Refresh()
    {
        _state = ProfileEditorState.Compute(Inputs());

        var pickable = CanEdit;

        Sync(AvailableRows, _availableRows, _state.Available, _state.AvailableShown, x => x.ModId,
            x => Watched(new AvailableModRowViewModel(_repo.Id, x, _itemFactory, OnVersionPicked), AvailableSelection),
            (row, x) => row.Update(x, pickable));

        Sync(PinnedRows, _pinnedRows, _state.Pinned, _state.PinnedShown, x => x.ModId,
            x => Watched(new PinnedModRowViewModel(_repo.Id, x, _itemFactory, OnVersionPicked, OnLockToggled), PinnedSelection),
            (row, x) => row.Update(x, pickable));

        StampMarks();

        Summary = Summarize();

        AvailableSelection.Recount();
        PinnedSelection.Recount();

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(UndoTooltip));
        OnPropertyChanged(nameof(RedoTooltip));
        OnSaveBlockChanged();

        NotifyCommands();

        OnPropertyChanged(nameof(IsWorthRetaining));
    }

    private ProfileModsEditorSummary Summarize() => new(
        Draft,
        _state,
        ShowIgnored,
        ApplyTarget is not null);

    /// <summary>Records an edit of the draft as one step, then shows it. Nothing is recorded while the draft may not change.</summary>
    private void Commit(ProfileDraft draft, string description)
    {
        if (CanEdit is false || draft.IsSameAs(Draft))
        {
            return;
        }

        _history.Push(Current with { Draft = draft }, description);

        Refresh();
    }

    /// <summary>Records a change to what is loaded as one step, then reads the catalog again.</summary>
    private Task CommitAsync(ProfileEditorSnapshot next, string description)
    {
        _history.Push(next, description);

        return RecomposeAsync();
    }

    /// <summary>Tells the catalog what is loaded and shown, reads it, and refreshes.</summary>
    private async Task RecomposeAsync()
    {
        Sources.ApplyToCatalog();

        if (Sources.HasEnabledSources is false && PinnedFilter is PinnedModFilter.NotInSources)
        {
            PinnedFilter = PinnedModFilter.All;
        }

        var generation = ++_recomposeGeneration;

        IsLoading = true;

        try
        {
            var snapshot = await _catalog.GetAsync(_cancellation.Token);
            var catalogView = new ProfileEditorCatalog(snapshot, _repo.Adapter.VersionComparer);

            await OnPageAsync(() =>
            {
                // A later read was asked for while this one ran, and it is the one that describes now.
                if (generation != _recomposeGeneration)
                {
                    return;
                }

                _catalogRead = catalogView;

                SearchCompleter.SetCatalogValues(snapshot.Known.SelectMany(x => x.Attributes));

                ShowRemote();

                Sources.LookUpRemote([.. catalogView.Index.Keys]);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "reading the mod list's sources");
        }
        finally
        {
            if (generation == _recomposeGeneration)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Places the remote sources' answers on the catalog as last read, and shows the result.</summary>
    private void ShowRemote()
    {
        _catalogView = _catalogRead.WithRemote(Sources.RemoteAnswers(), _downloadNotes.Find);

        Refresh();
        Sources.RebuildChips(_catalogView);
    }

    /// <summary>
    /// Makes <paramref name="shown"/> hold the rows for <paramref name="shownStates"/>, in order, reusing
    /// every row whose mod is still there so its thumbnail and selection survive.
    /// </summary>
    private static void Sync<TRow, TState>(
        BulkObservableCollection<TRow> shown,
        Dictionary<ModKey, TRow> rows,
        IReadOnlyList<TState> states,
        IReadOnlyList<TState> shownStates,
        Func<TState, ModKey> key,
        Func<TState, TRow> create,
        Action<TRow, TState> update)
        where TRow : class
    {
        var present = new HashSet<ModKey>();

        foreach (var state in states)
        {
            var modId = key(state);

            present.Add(modId);

            if (rows.TryGetValue(modId, out var row))
            {
                update(row, state);
            }
            else
            {
                row = create(state);
                update(row, state);
                rows[modId] = row;
            }
        }

        foreach (var gone in rows.Keys.Where(x => present.Contains(x) is false).ToList())
        {
            rows.Remove(gone);
        }

        shown.Reconcile([.. shownStates.Select(x => rows[key(x)])]);
    }

    /// <summary>
    /// Recounts a list's selection whenever one of its rows is picked or put down - by its own checkbox
    /// as well as by the list's gestures, which recount by themselves.
    /// </summary>
    private static TRow Watched<TRow>(TRow row, ModListSelection selection)
        where TRow : EditorModRowViewModel
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ISelectableRow.IsSelected))
            {
                selection.Recount();
            }
        };

        return row;
    }

    private void NotifyCommands()
    {
        foreach (var command in _commands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(IgnoreSelectedText));
        OnPropertyChanged(nameof(UnignoreSelectedText));
        OnPropertyChanged(nameof(DismissSelectedVersionsText));
        OnPropertyChanged(nameof(DismissSelectedUpdatesText));
        OnPropertyChanged(nameof(UpdateSelectedText));
        OnPropertyChanged(nameof(RevertSelectedText));

        NotifyCommands();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchQuery = ModSearchQuery.Parse(value, _catalog.Attributes);

        Refresh();
    }

    partial void OnAvailableFilterChanged(AvailableModFilter value) => Refresh();

    partial void OnShowIgnoredChanged(bool value) => Refresh();

    partial void OnPinnedFilterChanged(PinnedModFilter value) => Refresh();

    partial void OnPhaseChanged(EditorPhase value) => Refresh();

    partial void OnApplyTargetChanged(Game? value) => Summary = Summarize();

    #endregion


    #region Undo

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task Undo() => Step(_history.Undo);

    private bool CanUndo() => _history.CanUndo && CanEdit;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private Task Redo() => Step(_history.Redo);

    private bool CanRedo() => _history.CanRedo && CanEdit;

    private Task Step(Func<ProfileEditorSnapshot> move)
    {
        if (CanEdit is false)
        {
            return Task.CompletedTask;
        }

        var loaded = Current.Loaded;

        move();

        if (Current.Loaded.SetEquals(loaded))
        {
            Refresh();

            return Task.CompletedTask;
        }

        return RecomposeAsync();
    }

    #endregion


    #region The left list

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Add(AvailableModRowViewModel? row)
    {
        if (row is not null)
        {
            AddRows([row]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSelected))]
    private void AddSelected() => AddRows(AvailableSelection.Picked());

    private bool CanAddSelected() => CanEdit && AvailableSelection.HasSelection;

    [RelayCommand(CanExecute = nameof(CanAddAllShown))]
    private void AddAllShown() => AddRows([.. AvailableRows]);

    private bool CanAddAllShown() => CanEdit && AvailableRows.Count > 0;

    private void AddRows(IReadOnlyList<ISelectableRow> picked)
    {
        var rows = picked.OfType<AvailableModRowViewModel>().ToList();

        Commit(
            Draft.Pin(rows.Select(x => new ProfileModPin(x.ModId, x.Version.VersionId, new ProfileModLock(false, x.State.LockedBySource)))),
            rows.Count == 1 ? $"Added {rows[0].Name}" : $"Added {ProfileModsEditorSummary.Mods(rows.Count)}");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ToggleIgnore(AvailableModRowViewModel? row)
    {
        if (row is not null)
        {
            SetIgnored([row], row.IsIgnored is false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanIgnoreSelected))]
    private void IgnoreSelected() => SetIgnored([.. PickedAvailable().Where(x => x.IsIgnored is false)], true);

    private bool CanIgnoreSelected() => CanEdit && PickedAvailable().Any(x => x.IsIgnored is false);

    [RelayCommand(CanExecute = nameof(CanUnignoreSelected))]
    private void UnignoreSelected() => SetIgnored([.. PickedAvailable().Where(x => x.IsIgnored)], false);

    private bool CanUnignoreSelected() => CanEdit && PickedAvailable().Any(x => x.IsIgnored);

    private void SetIgnored(IReadOnlyList<AvailableModRowViewModel> rows, bool ignored)
    {
        foreach (var row in rows)
        {
            row.IsSelected = false;
        }

        var what = rows.Count == 1 ? rows[0].Name : ProfileModsEditorSummary.Mods(rows.Count);

        Commit(Draft.SetIgnored(rows.Select(x => x.ModId), ignored), ignored ? $"Ignored {what}" : $"Stopped ignoring {what}");
    }

    /// <summary>Dismisses the version each picked row shows, so it is never offered as an update.</summary>
    [RelayCommand(CanExecute = nameof(CanDismissSelectedVersions))]
    private void DismissSelectedVersions() => Dismiss(VersionsToDismiss());

    private bool CanDismissSelectedVersions() => CanEdit && VersionsToDismiss().Count > 0;

    private List<(string Name, ModVersionIdentity Version)> VersionsToDismiss()
        => [.. PickedAvailable()
            .Select(x => (x.Name, x.Version.Identity))
            .Where(x => Draft.Dismissed.Contains(x.Identity) is false)];

    private List<AvailableModRowViewModel> PickedAvailable() => [.. AvailableSelection.Picked().OfType<AvailableModRowViewModel>()];

    #endregion


    #region The right list

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Remove(PinnedModRowViewModel? row)
    {
        if (row is not null)
        {
            RemoveRows([row]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected() => RemoveRows(PinnedSelection.Picked());

    private bool CanRemoveSelected() => CanEdit && PickedPinned().Any(x => x.IsTakenOut is false);

    [RelayCommand(CanExecute = nameof(CanRemoveAllShown))]
    private void RemoveAllShown() => RemoveRows([.. PinnedRows]);

    private bool CanRemoveAllShown() => CanEdit && PinnedRows.Any(x => x.IsTakenOut is false);

    private void RemoveRows(IReadOnlyList<ISelectableRow> picked)
    {
        var rows = picked.OfType<PinnedModRowViewModel>().Where(x => x.IsTakenOut is false).ToList();

        Commit(
            Draft.Remove(rows.Select(x => x.ModId)),
            rows.Count == 1 ? $"Removed {rows[0].Name}" : $"Removed {ProfileModsEditorSummary.Mods(rows.Count)}");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Revert(PinnedModRowViewModel? row)
    {
        if (row is not null)
        {
            RevertRows([row]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRevertSelected))]
    private void RevertSelected() => RevertRows(PickedPinned());

    private bool CanRevertSelected() => CanEdit && PickedPinned().Any(x => x.IsChanged);

    [RelayCommand(CanExecute = nameof(CanRevertAllShown))]
    private void RevertAllShown() => RevertRows([.. PinnedRows]);

    private bool CanRevertAllShown() => CanEdit && PinnedRows.Any(x => x.IsChanged);

    private void RevertRows(IReadOnlyList<PinnedModRowViewModel> picked)
    {
        var rows = picked.Where(x => x.IsChanged).ToList();

        Commit(
            Draft.Revert(rows.Select(x => x.ModId)),
            rows.Count == 1 ? $"Reverted {rows[0].Name}" : $"Reverted {ProfileModsEditorSummary.Mods(rows.Count)}");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void LockSelected() => SetLocked(true);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void UnlockSelected() => SetLocked(false);

    private void SetLocked(bool locked)
    {
        var rows = PickedPinned().Where(x => x.IsTakenOut is false && x.LockedByProfile != locked).ToList();
        var what = rows.Count == 1 ? rows[0].Name : ProfileModsEditorSummary.Mods(rows.Count);

        Commit(Draft.SetLocked(rows.Select(x => x.ModId), locked), locked ? $"Locked {what}" : $"Unlocked {what}");
    }

    private void OnLockToggled(PinnedModRowViewModel row, bool locked)
    {
        if (row.IsTakenOut || CanEdit is false)
        {
            row.RestoreLock();

            return;
        }

        Commit(Draft.SetLocked([row.ModId], locked), locked ? $"Locked {row.Name}" : $"Unlocked {row.Name}");
    }

    private async void OnVersionPicked(EditorModRowViewModel row, ProfileModVersionOption option)
    {
        if (row is AvailableModRowViewModel)
        {
            _availableChoices[row.ModId] = option.Version.VersionId;

            Refresh();

            return;
        }

        if (row is not PinnedModRowViewModel pinned
            || pinned.IsTakenOut
            || CanEdit is false
            || (pinned.IsLocked && await ConfirmLockedVersionChangeAsync(pinned, option.Version.VersionId) is false))
        {
            return;
        }

        Commit(Draft.SetVersion(row.ModId, option.Version.VersionId), $"Changed {row.Name} to {option.Version.VersionId}");
    }

    private List<PinnedModRowViewModel> PickedPinned() => [.. PinnedSelection.Picked().OfType<PinnedModRowViewModel>()];

    #endregion


    #region Updates

    [RelayCommand(CanExecute = nameof(CanApplyAllUpdates))]
    private void ApplyAllUpdates() => ApplyUpdates(_state.Updates.Available);

    private bool CanApplyAllUpdates() => CanEdit && _state.Updates.Available.Count > 0;

    [RelayCommand(CanExecute = nameof(CanApplyRepoUpdates))]
    private void ApplyRepoUpdates() => ApplyUpdates([.. _state.Updates.Available.Where(x => x.ImportsOnSave is false)]);

    private bool CanApplyRepoUpdates() => CanEdit && _state.Updates.FreeCount > 0;

    [RelayCommand(CanExecute = nameof(CanUpdateSelected))]
    private void UpdateSelected()
    {
        var picked = PickedPinned().Where(x => x.State.Update is not null).ToList();
        var locked = picked.Count(x => x.IsLocked);

        ApplyUpdates([.. picked.Where(x => x.IsLocked is false).Select(x => x.State.Update!)]);

        if (locked > 0)
        {
            _toasts.Show(locked == 1 ? "1 locked mod was skipped." : $"{locked} locked mods were skipped.");
        }
    }

    private bool CanUpdateSelected() => CanEdit && PickedPinned().Any(x => x.HasUpdate);

    private void ApplyUpdates(IReadOnlyList<ProfileModUpdate> updates)
    {
        Commit(
            Draft.Pin(updates.Where(x => Draft.Pins.ContainsKey(x.ModId)).Select(x => Draft.Pins[x.ModId] with { VersionId = x.To })),
            $"Updated {ProfileModsEditorSummary.Mods(updates.Count)}");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task UpdateOne(PinnedModRowViewModel? row)
    {
        if (row?.State.Update is not ProfileModUpdate update)
        {
            return;
        }

        if (row.IsLocked && await ConfirmLockedVersionChangeAsync(row, update.To) is false)
        {
            return;
        }

        Commit(Draft.SetVersion(row.ModId, update.To), $"Updated {row.Name} to {update.To}");
    }

    /// <summary>Dismisses the version each picked row's update points at, locked or not.</summary>
    [RelayCommand(CanExecute = nameof(CanDismissSelectedUpdates))]
    private void DismissSelectedUpdates() => Dismiss(UpdatesToDismiss());

    private bool CanDismissSelectedUpdates() => CanEdit && UpdatesToDismiss().Count > 0;

    private List<(string Name, ModVersionIdentity Version)> UpdatesToDismiss()
        => [.. PickedPinned()
            .Where(x => x.State.Update is not null)
            .Select(x => (x.Name, new ModVersionIdentity(x.ModId, x.State.Update!.To)))];

    private void Dismiss(IReadOnlyList<(string Name, ModVersionIdentity Version)> versions)
    {
        Commit(
            Draft.Dismiss(versions.Select(x => x.Version)),
            versions is [var only] ? $"Dismissed {only.Name} {only.Version.VersionId}" : $"Dismissed {versions.Count} versions");
    }

    /// <summary>Every dismissed version, each with a box to offer it as an update again.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ShowDismissed()
    {
        if (Draft.Dismissed.IsEmpty)
        {
            return;
        }

        var modal = new ProfileDismissedVersionsModalViewModel([.. Draft.Dismissed
            .Select(x => new ProfileDismissedVersionViewModel(NameOf(x.ModId), x))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Identity.ModId.Value, StringComparer.Ordinal)
            .ThenBy(x => x.Identity.VersionId.Value, StringComparer.Ordinal)]);

        await _modalService.Show(modal);

        if (modal.Result.Count > 0)
        {
            Commit(
                Draft.Restore(modal.Result),
                modal.Result.Count == 1 ? $"Restored {NameOf(modal.Result[0].ModId)} {modal.Result[0].VersionId}" : $"Restored {modal.Result.Count} versions");
        }
    }

    private string NameOf(ModKey modId)
        => _pinnedRows.GetValueOrDefault(modId)?.Name ?? _availableRows.GetValueOrDefault(modId)?.Name ?? modId.Value;

    /// <summary>The locked mods with a newer version, each with a box to move it anyway.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ShowSkippedUpdates()
    {
        var skipped = _state.Updates.Skipped;

        if (skipped.Count == 0)
        {
            return;
        }

        var modal = new ProfileLockedUpdatesModalViewModel([.. skipped
            .Select(x => new ProfileLockedUpdateViewModel(NameOf(x.ModId), x))]);

        await _modalService.Show(modal);

        ApplyUpdates([.. skipped.Where(x => modal.Result.Contains(x.ModId))]);
    }

    [RelayCommand]
    private void ShowUpdates() => PinnedFilter = PinnedModFilter.Updates;

    [RelayCommand]
    private void ShowChanges() => PinnedFilter = PinnedModFilter.Changes;

    /// <summary>Asks before moving a locked pin, with why it is locked, because that decides the answer.</summary>
    private async Task<bool> ConfirmLockedVersionChangeAsync(PinnedModRowViewModel row, ModVersionKey target)
    {
        var reason = row.State.Lock.Source switch
        {
            ProfileModLockSource.Adapter =>
                "Version-sensitive. Changing it partway through a savegame can break that savegame.",
            ProfileModLockSource.Profile =>
                "Locked in this profile.",
            _ =>
                "Version-sensitive and locked in this profile. Changing it partway through a savegame can break that savegame.",
        };

        var confirmation = new ConfirmationModalViewModel(
            "This mod is locked",
            $"'{row.Name}' is locked at {row.Version.VersionId}.\n\n{reason}\n\n"
                + $"Change it to {target}?",
            IconKind.Warning,
            "Change version",
            "Cancel");

        await _modalService.Show(confirmation);

        return confirmation.Result;
    }

    #endregion


    #region Saving

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveChanges()
    {
        var apply = _skipApplyOnce is false;
        _skipApplyOnce = false;

        var draft = Draft;
        var pending = _state.Pinned.Where(x => x.IsPending).ToList();

        var request = new ProfileSaveRequest(
            _repo,
            _profile.Id,
            _profile.Name,
            _saveRequestIds.For(new SaveAttempt(draft, VersionDescription)),
            _basedOn,
            VersionDescription,
            [.. draft.Saved.Values],
            [.. _state.Pinned.Where(x => x.Pin is not null).Select(x => x.Pin!)],
            [.. draft.SavedIgnored],
            [.. draft.IgnoredToWrite],
            [.. draft.SavedDismissed],
            [.. draft.Dismissed],
            _ignoredVersion,
            [.. pending.Select(x => x.Version)],
            pending.ToDictionary(x => x.Version.Identity, x => x.Version.Name),
            _catalog,
            apply && draft.HasPinChanges);

        RetireActivationOffer();

        foreach (var row in _pinnedRows.Values)
        {
            row.Item.ResetImportState();
        }

        var run = _saveService.Start(request);

        _markedRun = run;
        run.Advanced += OnRunAdvanced;

        try
        {
            await WatchAsync(run.Completion, request.Apply);
        }
        finally
        {
            run.Advanced -= OnRunAdvanced;
        }
    }

    private bool CanSave() => CanEdit && HasUnsavedChanges && CanImportHere;

    /// <summary>Runs the same save once without re-applying afterwards.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveOnly))]
    private Task SaveOnly()
    {
        _skipApplyOnce = true;

        return SaveChangesCommand.ExecuteAsync(null);
    }

    private bool CanSaveOnly() => CanSave() && Summary.WillApply;

    /// <summary>
    /// Follows a save to its end. The save belongs to the profile rather than to this page, so a page
    /// closed meanwhile stops following it and touches nothing.
    /// </summary>
    private async Task WatchAsync(Task<ProfileSaveOutcome> completion, bool applied)
    {
        Phase = EditorPhase.Saving;

        ProfileSaveOutcome outcome;

        try
        {
            outcome = await completion.WaitAsync(_cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            return;
        }

        // The newer list was chosen over this draft, so it is what the page edits from now on.
        if (outcome.NeedsReload)
        {
            await ReloadAsync();

            return;
        }

        // A failed save leaves the draft as it is, still unsaved, with the rows that did not make it
        // marked - so pressing Save again once the cause is fixed is the whole recovery.
        if (outcome.Succeeded is false)
        {
            Phase = EditorPhase.Editing;

            return;
        }

        if (outcome.RevisionWritten)
        {
            VersionDescription = "";
        }

        await ReloadAsync();

        if (applied && outcome.ApplyMessage is null && CanEdit && _suspended is false)
        {
            OfferActivation();
        }
    }

    private void OnRunAdvanced() => _ = OnPageAsync(StampMarks);

    /// <summary>Puts what the last save reported onto the rows of the versions it imported.</summary>
    private void StampMarks()
    {
        if (_markedRun is not ProfileSaveRun run)
        {
            return;
        }

        var items = _pinnedRows.Values.ToDictionary(x => x.Version.Identity, x => x.Item);

        foreach (var progress in run.Progress)
        {
            items.GetValueOrDefault(progress.Identity)?.Apply(progress);
        }

        foreach (var result in run.Results)
        {
            items.GetValueOrDefault(result.Identity)?.Apply(result);
        }
    }

    /// <summary>Takes the draft back to the saved profile. Undoable, so it does not ask.</summary>
    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void DiscardChanges() => Commit(Draft.RevertAll(), "Discarded changes");

    private bool CanDiscard() => CanEdit && HasUnsavedChanges;

    /// <summary>For a profile nothing is using yet: offered after a save that had nothing to apply to.</summary>
    private void OfferActivation()
    {
        RetireActivationOffer();

        if (_repo.Games.FirstOrDefault() is not Game game)
        {
            return;
        }

        _activationToast = _toasts.Show(
            "Activate this profile?",
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
            // Not stopped by closing the page: the apply has its own place on the strip.
            var outcome = await _applyService.ActivateAsync(
                _repo,
                game,
                _profile.Id,
                _profile.Name,
                confirmPlan: true,
                progress: null,
                CancellationToken.None);

            if (outcome.Activated)
            {
                await OnPageAsync(RefreshApplyTargets);
            }

            _toasts.Show(outcome.Message, outcome.ToastSeverity);

            await _driftMonitor.CheckAsync();
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, $"putting '{_profile.Name}' on '{game.Name}'");
        }
    }

    private void OnLeasesChanged(object? sender, EventArgs e) => _ = OnPageAsync(() =>
    {
        OnSaveBlockChanged();
        NotifyCommands();
    });

    private void OnSaveBlockChanged()
    {
        OnPropertyChanged(nameof(CanImportHere));
        OnPropertyChanged(nameof(SaveBlockedReason));
        OnPropertyChanged(nameof(HasSaveBlockedReason));
    }

    #endregion


    #region Bringing a list in from somewhere else

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task CopyFromProfile()
    {
        try
        {
            var modal = new CopyProfileModsModalViewModel(await _otherProfiles.ListAsync(_cancellation.Token), ProfileName);

            await _modalService.Show(modal);

            if (modal.Result is not Profile source)
            {
                return;
            }

            var pins = await _otherProfiles.ReadPinsAsync(source.Id, _cancellation.Token);

            if (modal.ResultMode is CopyProfileModsMode.Replace)
            {
                Commit(Draft.ReplacePins(pins), $"Replaced the list with {source.Name}'s");

                return;
            }

            var missing = pins.Where(x => Draft.Pins.ContainsKey(x.ModId) is false).ToList();

            if (missing.Count == 0)
            {
                _toasts.Show("Nothing to copy");

                return;
            }

            Commit(Draft.Pin(missing), $"Copied {ProfileModsEditorSummary.Mods(missing.Count)} from {source.Name}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "copying a mod list from another profile");
        }
    }

    /// <summary>Turns a pasted list into a selection on the left. It picks and reports; it never adds.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task PasteList()
    {
        var modal = new PasteModListModalViewModel();

        await _modalService.Show(modal);

        if (modal.Result.Count > 0)
        {
            SelectPasted(modal.Result);
        }
    }

    private void SelectPasted(IReadOnlyList<string> terms)
    {
        SearchText = string.Empty;
        AvailableFilter = AvailableModFilter.All;

        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _state.Pinned.Where(x => x.IsTakenOut is false))
        {
            pinned.Add(row.ModId.Value);
            pinned.Add(row.Version.Name);
        }

        var byId = new Dictionary<string, AvailableModRowViewModel>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, AvailableModRowViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _availableRows.Values)
        {
            byId.TryAdd(row.ModId.Value, row);
            byName.TryAdd(row.Name, row);
        }

        var matched = new HashSet<AvailableModRowViewModel>();
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

        AvailableSelection.ClearSelection();

        foreach (var row in matched)
        {
            row.IsSelected = true;
        }

        AvailableSelection.Recount();

        _toasts.Show(DescribePaste(terms.Count, matched.Count, already, missing));
    }

    private static string DescribePaste(int asked, int matched, int already, IReadOnlyList<string> missing)
    {
        var names = asked == 1 ? "name" : "names";

        var text = matched == 0
            ? $"No matches for the {asked} {names} pasted."
            : $"Selected {matched} of {asked} {names}.";

        if (already > 0)
        {
            text += already == 1 ? " 1 was already in this profile." : $" {already} were already in this profile.";
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

    #endregion


    #region Sources

    /// <summary>
    /// Makes the game's mod folders the only sources and shows <paramref name="filter"/>, for a page
    /// opened to review drift. Called before the page loads, and again if it is already open.
    /// </summary>
    public void Review(PinnedModFilter filter)
    {
        var loaded = Sources.FocusOnTargets();

        PinnedFilter = filter;

        if (IsLoading && _history.CanUndo is false)
        {
            _history.Reset(Current with { Loaded = loaded });
        }
        else if (loaded.SetEquals(Current.Loaded))
        {
            _ = RecomposeAsync();
        }
        else
        {
            _ = CommitAsync(Current with { Loaded = loaded }, "Loaded the game's mod folders");
        }
    }

    /// <summary>An apply changed a mod folder, so a scan of it is stale. The draft is left alone.</summary>
    private void OnModFolderChanged(string folder)
    {
        if (_cancellation.IsCancellationRequested || _catalog.RescanFolder(folder) is false)
        {
            return;
        }

        _ = Application.Current?.Dispatcher.InvokeAsync(RecomposeAsync);
    }

    #endregion


    #region Loading

    protected override Task InitAsync() => Application.Current.Dispatcher.InvokeAsync(LoadAsync).Task.Unwrap();

    /// <summary>On the UI thread, like everything else that changes the page.</summary>
    private async Task LoadAsync()
    {
        // A save of this profile that this page did not start: wait it out read-only, then read what it wrote.
        if (_saveService.Find(_profile.Id) is ProfileSaveRun run)
        {
            Phase = EditorPhase.WaitingForSave;

            try
            {
                await run.Completion.WaitAsync(_cancellation.Token);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
        }

        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(HasLoadFailed))]
    private Task LoadAgain() => ReloadAsync();

    protected override void OnInitFailed(Exception ex)
    {
        if (ex is not OperationCanceledException)
        {
            base.OnInitFailed(ex);
        }
    }

    /// <summary>
    /// Reads the profile from the server and starts a new draft from it: on opening, after a save, and
    /// when asked again after a read failed. What is loaded stays loaded. Runs on the UI thread.
    /// </summary>
    /// <remarks>
    /// A failed read leaves nothing to edit: a draft based on no revision would save over everything
    /// the profile pins.
    /// </remarks>
    private async Task ReloadAsync()
    {
        Phase = EditorPhase.Loading;
        IsLoading = true;

        try
        {
            var modListRead = _pinsClient.GetProfileModPinsV1Async(_repo.Id, _profile.Id, null, _cancellation.Token);
            var ignoredRead = _profilesClient.GetProfileIgnoreListV1Async(_repo.Id, _profile.Id, _cancellation.Token);

            var modList = await modListRead;
            var ignored = await ignoredRead;

            _basedOn = modList.Revision;
            _ignoredVersion = ignored.Version;
            _markedRun = null;
            _savedDates = modList.Pins.ToDictionary(
                x => ModKey.From(x.ModId),
                x => new SavedPinDate(ModVersionKey.From(x.ModVersionId), x.Added));

            _history.Reset(Current with
            {
                Draft = new ProfileDraft(
                    OtherProfilesReader.ToPins(modList),
                    ignored.ModIds.Select(ModKey.From),
                    ignored.DismissedVersions.Select(x => new ModVersionIdentity(ModKey.From(x.ModId), ModVersionKey.From(x.VersionId))))
            });

            Phase = EditorPhase.Editing;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Phase = EditorPhase.LoadFailed;
            IsLoading = false;

            await _errorReporter.ShowAsync(exception, $"reading the mod list of '{_profile.Name}'");

            return;
        }

        await RecomposeAsync();
    }

    /// <summary>Runs <paramref name="work"/> on the UI thread, unless the page has been closed by then.</summary>
    private Task OnPageAsync(Action work) => Application.Current.Dispatcher.InvokeAsync(() =>
    {
        if (_cancellation.IsCancellationRequested is false)
        {
            work();
        }
    }).Task;

    #endregion


    #region Apply target

    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshApplyTargets();

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Game.ActiveProfile))
        {
            RefreshApplyTargets();
        }
    }

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

    #endregion


    #region Kept alive

    public void Suspend()
    {
        _suspended = true;

        RetireActivationOffer();
        _notices.Release(_activeProfile);
    }

    public void Resume()
    {
        _suspended = false;

        _notices.SuppressFor(_activeProfile);
    }

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Profile.Name))
        {
            _ = OnPageAsync(() => OnPropertyChanged(nameof(ProfileName)));
        }
    }

    private void OnRepoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Repo.MembershipLevel))
        {
            _ = OnPageAsync(() => OnPropertyChanged(nameof(IsWorthRetaining)));
        }
    }

    private void OnReposChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _ = OnPageAsync(() => OnPropertyChanged(nameof(IsWorthRetaining)));

    private void OnProfilesChanged(Guid repoId)
    {
        if (repoId == _repo.Id)
        {
            _ = OnPageAsync(() => OnPropertyChanged(nameof(IsWorthRetaining)));
        }
    }

    #endregion


    public void Dispose()
    {
        if (_suspended is false)
        {
            _notices.Release(_activeProfile);
        }

        _leases.Changed -= OnLeasesChanged;
        _syncService.ModFolderChanged -= OnModFolderChanged;
        _repo.Games.CollectionChanged -= OnGamesChanged;
        _profile.PropertyChanged -= OnProfileChanged;
        _repo.PropertyChanged -= OnRepoChanged;
        _repoStore.Repos.CollectionChanged -= OnReposChanged;
        _profileStore.Changed -= OnProfilesChanged;

        // The save belongs to the profile, not to this page, so it is not stopped here.
        RetireActivationOffer();

        foreach (var game in _watchedGames)
        {
            game.PropertyChanged -= OnGameChanged;
        }

        _watchedGames.Clear();

        _cancellation.Cancel();
        _catalog.Dispose();
    }


    private static string Counted(string verb, int count) => count > 0 ? $"{verb} {count}" : verb;


    /// <summary>A save is the same request while the draft has no edits since and the label is the same.</summary>
    private sealed record SaveAttempt(ProfileDraft Draft, string? Label)
    {
        public bool Equals(SaveAttempt? other)
            => other is not null && Draft.IsSameAs(other.Draft) && Label == other.Label;

        public override int GetHashCode() => Label?.GetHashCode() ?? 0;
    }


    public class Factory(IServiceProvider serviceProvider, IRetainedPages retainedPages)
    {
        /// <summary>The page kept for this profile where there is one, and a new one otherwise.</summary>
        /// <param name="review">Opens the page reviewing the game's mod folders. Null opens it with the default sources on.</param>
        public ProfileModsEditorPageViewModel Create(Repo repo, Profile profile, PinnedModFilter? review = null)
        {
            var page = retainedPages.Take<ProfileModsEditorPageViewModel>(new ProfileModsEditorKey(repo.Id, profile.Id))
                ?? ActivatorUtilities.CreateInstance<ProfileModsEditorPageViewModel>(serviceProvider, repo, profile);

            // Here rather than through the constructor: a nullable value does not survive
            // ActivatorUtilities' positional matching.
            if (review is PinnedModFilter filter)
            {
                page.Review(filter);
            }

            return page;
        }
    }
}


/// <summary>Which profile's mod list an editor page is for, as <see cref="IRetainedPages"/> keeps it.</summary>
public sealed record ProfileModsEditorKey(Guid RepoId, Guid ProfileId);


public enum EditorPhase
{
    Loading,
    LoadFailed,
    Editing,
    WaitingForSave,
    Saving
}
