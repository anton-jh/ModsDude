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
/// save, and a save whose import does not fully succeed writes nothing. See
/// docs/09-mod-catalog.md#profile-mod-list-editor.
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
    private readonly NavigationLockService _navigationLock;
    private readonly GameRepository _gameRepository;
    private readonly ProfileApplyService _applyService;
    private readonly ModSyncService _syncService;
    private readonly DriftMonitor _driftMonitor;
    private readonly NoticeCenterViewModel _notices;
    private readonly IResourceLeases _leases;
    private readonly IToastService _toasts;
    private readonly ActiveProfile _activeProfile;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<Game> _watchedGames = [];
    private readonly IReadOnlyList<IRelayCommand> _commands;

    private readonly EditHistory<ProfileEditorSnapshot> _history = new(new(new ProfileDraft([], []), []));
    private readonly OtherProfilesReader _otherProfiles;
    private readonly Dictionary<ModKey, ModVersionKey> _availableChoices = [];

    private readonly Dictionary<ModKey, AvailableModRowViewModel> _availableRows = [];
    private readonly Dictionary<ModKey, PinnedModRowViewModel> _pinnedRows = [];

    private ProfileEditorCatalog _catalogView;
    private ProfileEditorState _state;
    private IReadOnlyDictionary<ModKey, SavedPinDate> _savedDates = new Dictionary<ModKey, SavedPinDate>();
    private ModSearchQuery _searchQuery = ModSearchQuery.Empty;
    private int _basedOn;
    private int _recomposeGeneration;
    private bool _skipApplyOnce;
    private ProfileSaveRun? _markedRun;
    private IToast? _activationToast;


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
        IFilePickerService filePickerService,
        NavigationLockService navigationLock,
        GameRepository gameRepository,
        ProfileApplyService applyService,
        ModSyncService syncService,
        DriftMonitor driftMonitor,
        NoticeCenterViewModel notices,
        IResourceLeases leases,
        IToastService toasts)
    {
        _repo = repo;
        _profile = profile;
        _itemFactory = itemFactory;
        _saveService = saveService;
        _dependenciesClient = dependenciesClient;
        _profilesClient = profilesClient;
        _modalService = modalService;
        _errorReporter = errorReporter;
        _navigationLock = navigationLock;
        _gameRepository = gameRepository;
        _applyService = applyService;
        _syncService = syncService;
        _driftMonitor = driftMonitor;
        _notices = notices;
        _leases = leases;
        _toasts = toasts;
        _activeProfile = new ActiveProfile(repo.Id, profile.Id);

        _catalog = catalogFactory.Create(repo);
        _catalogView = ProfileEditorCatalog.Empty(repo.Adapter.VersionComparer);
        _otherProfiles = new OtherProfilesReader(profilesClient, dependenciesClient, repo.Id, profile.Id);

        Sources = new EditorSourcesViewModel(
            _catalog, _otherProfiles, filePickerService, modalService, errorReporter,
            () => _history.Current, CommitAsync, RecomposeAsync, _cancellation.Token);

        RemoteUpdates = new RemoteUpdatesViewModel(
            repo.Adapter.GetBaseCapabilityAdapterFactory<IRemoteUpdatesAdapter>()?.Invoke().Providers ?? [],
            errorReporter,
            _cancellation.Token);

        RemoteUpdates.Changed += Refresh;
        RemoteUpdates.DownloadsWanted += () => _ = Sources.EnableDownloadsAsync();

        _state = ProfileEditorState.Compute(Inputs());
        _summary = Summarize();

        SearchCompleter = new ModSearchCompleter(_catalog.Attributes);
        SortAttributes = [.. _catalog.Attributes.Select(x => x.Key)];
        ProfileName = profile.Name;

        AvailableSorting.Changed += Refresh;
        PinnedSorting.Changed += Refresh;

        AvailableSelection = new ModListSelection(() => AvailableRows, () => [.. _availableRows.Values], AddRows, "Add");
        PinnedSelection = new ModListSelection(() => PinnedRows, () => [.. _pinnedRows.Values], RemoveRows, "Take out");
        AvailableSelection.Changed += OnSelectionChanged;
        PinnedSelection.Changed += OnSelectionChanged;

        _commands = [.. GetType().GetProperties()
            .Where(x => typeof(IRelayCommand).IsAssignableFrom(x.PropertyType))
            .Select(x => (IRelayCommand)x.GetValue(this)!)];

        _syncService.ModFolderChanged += OnModFolderChanged;
        _repo.Games.CollectionChanged += OnGamesChanged;
        _leases.Changed += OnLeasesChanged;

        // Somebody already looking at the drifted profile's mod list does not need to be told about it.
        _notices.SuppressFor(_activeProfile);

        RefreshApplyTargets();
    }


    public string ProfileName { get; }

    public EditorSourcesViewModel Sources { get; }

    public RemoteUpdatesViewModel RemoteUpdates { get; }

    public BulkObservableCollection<AvailableModRowViewModel> AvailableRows { get; } = [];
    public BulkObservableCollection<PinnedModRowViewModel> PinnedRows { get; } = [];

    public ModListSelection AvailableSelection { get; }
    public ModListSelection PinnedSelection { get; }

    public ListSortViewModel AvailableSorting { get; } =
        new(ModListSortKind.Name, "Date imported", "When the version was imported into the repo. Versions not in the repo count as newest.");

    public ListSortViewModel PinnedSorting { get; } =
        new(ModListSortKind.Name, "Date added", "When the mod entered the profile, or last moved to another version. Unsaved changes count as newest.");

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

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isSaving;

    /// <summary>A save of this profile that this page did not start is running; the page waits for it.</summary>
    [ObservableProperty]
    private bool _isWaitingForSave;

    /// <summary>
    /// Whether nothing may change the draft. The lists stay scrollable and readable; every control that
    /// writes binds to <see cref="CanEdit"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isReadOnly;

    public bool CanEdit => IsReadOnly is false;

    /// <summary>What to call this save in the profile's history. Optional.</summary>
    [ObservableProperty]
    private string _versionDescription = "";

    [ObservableProperty]
    private Game? _applyTarget;

    public bool HasUnsavedChanges => _history.Current.Draft.HasChanges;

    public string UndoTooltip => _history.UndoDescription is string what ? $"Undo: {what} (Ctrl+Z)" : "Nothing to undo";
    public string RedoTooltip => _history.RedoDescription is string what ? $"Redo: {what} (Ctrl+Y)" : "Nothing to redo";

    public string SaveOnlyDescription =>
        "Saves the profile but leaves your installed mods untouched. Your locked mods stay at the versions " +
        "the game updated them to. Only if you know exactly what you are doing.";

    public string UpdateRepoOnlyDescription =>
        "Moves only the pins whose newer version this repo already holds, so nothing is uploaded.";

    public string IgnoreSelectedText => Counted("Ignore", PickedAvailable().Count(x => x.IsIgnored is false));
    public string UnignoreSelectedText => Counted("Stop ignoring", PickedAvailable().Count(x => x.IsIgnored));
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
    public bool CanImportHere => _state.PendingCount == 0 || _saveService.DescribeImportBusy(_repo.Id) is null;

    public string? SaveBlockedReason
        => _state.PendingCount > 0 && _saveService.DescribeImportBusy(_repo.Id) is string holder
            ? $"{(_state.PendingCount == 1 ? "1 mod here needs importing" : $"{_state.PendingCount} mods here need importing")}, and {holder.ToLowerInvariant()}."
            : null;

    public bool HasSaveBlockedReason => SaveBlockedReason is not null;


    private ProfileEditorSnapshot Current => _history.Current;

    private ProfileDraft Draft => _history.Current.Draft;


    #region The one path

    private ProfileEditorInputs Inputs() => new(_history.Current.Draft, _catalogView)
    {
        IncludeRegistered = Sources.IncludeRegistered,
        ProfileSources = Sources.EnabledProfiles,
        RemoteUpdates = RemoteUpdates.Answers(),
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

        var pickable = IsReadOnly is false;

        Sync(AvailableRows, _availableRows, _state.Available, _state.AvailableShown, x => x.ModId,
            x => Watched(new AvailableModRowViewModel(_repo.Id, x, _itemFactory, OnVersionPicked), AvailableSelection),
            (row, x) => row.Update(x, RemoteUpdates.Show(x.RemoteUpdate), pickable));

        Sync(PinnedRows, _pinnedRows, _state.Pinned, _state.PinnedShown, x => x.ModId,
            x => Watched(new PinnedModRowViewModel(_repo.Id, x, _itemFactory, OnVersionPicked, OnLockToggled), PinnedSelection),
            (row, x) => row.Update(x, RemoteUpdates.Show(x.RemoteUpdate), pickable));

        StampMarks();

        Summary = Summarize();

        AvailableSelection.Recount();
        PinnedSelection.Recount();

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(UndoTooltip));
        OnPropertyChanged(nameof(RedoTooltip));
        OnSaveBlockChanged();

        if (HasUnsavedChanges)
        {
            _navigationLock.AcquireLock(this);
        }
        else
        {
            _navigationLock.ReleaseLock(this);
        }

        NotifyCommands();
    }

    private ProfileModsEditorSummary Summarize() => new(
        Draft,
        _state,
        ShowIgnored,
        RemoteUpdates.Name,
        ApplyTarget is not null);

    /// <summary>Records an edit of the draft as one step, then shows it.</summary>
    private void Commit(ProfileDraft draft, string description)
    {
        if (draft.IsSameAs(Draft))
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

            await OnUiThreadAsync(() =>
            {
                // A later read was asked for while this one ran, and it is the one that describes now.
                if (generation != _recomposeGeneration)
                {
                    return;
                }

                _catalogView = catalogView;

                SearchCompleter.SetCatalogValues(snapshot.Known.SelectMany(x => x.Attributes));

                RemoteUpdates.LookUp(catalogView.Index.Keys);
                Refresh();
                Sources.RebuildChips(catalogView);
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (generation == _recomposeGeneration)
            {
                IsLoading = false;
            }
        }
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

    partial void OnIsReadOnlyChanged(bool value) => Refresh();

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
            rows.Count == 1 ? $"Took out {rows[0].Name}" : $"Took out {ProfileModsEditorSummary.Mods(rows.Count)}");
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
        if (row.IsTakenOut || IsReadOnly)
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
            || IsReadOnly
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

    /// <summary>The locked mods the batch left alone, each with a box to move it anyway.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ShowSkippedUpdates()
    {
        var skipped = _state.Updates.Skipped;

        if (skipped.Count == 0)
        {
            return;
        }

        var modal = new ProfileLockedUpdatesModalViewModel([.. skipped
            .Select(x => new ProfileLockedUpdateViewModel(_pinnedRows.GetValueOrDefault(x.ModId)?.Name ?? x.ModId.Value, x))]);

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
                "The game adapter reads it as version-sensitive - a map, typically - so changing its version "
                    + "partway through a save can corrupt that save.",
            ProfileModLockSource.Profile =>
                "You locked it in this profile. Other profiles are not affected either way.",
            _ =>
                "The game adapter reads it as version-sensitive and you have locked it in this profile as well. "
                    + "Changing its version partway through a save can corrupt that save.",
        };

        var confirmation = new ConfirmationModalViewModel(
            "This mod is locked",
            $"'{row.Name}' is pinned at {row.Version.VersionId} and locked.\n\n{reason}\n\n"
                + $"Change '{row.Name}' to {target} in this profile?",
            IconKind.Warning,
            "Change the version",
            "Leave it alone");

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
            _basedOn,
            VersionDescription,
            [.. draft.Saved.Values],
            [.. _state.Pinned.Where(x => x.Pin is not null).Select(x => x.Pin!)],
            [.. draft.SavedIgnored],
            [.. draft.IgnoredToWrite],
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

    private async Task WatchAsync(Task<ProfileSaveOutcome> completion, bool applied)
    {
        IsSaving = true;
        IsReadOnly = true;

        try
        {
            var outcome = await completion;

            // A failed save leaves the draft as it is, still unsaved, with the rows that did not make it
            // marked - so pressing Save again once the cause is fixed is the whole recovery.
            if (outcome.Succeeded is false)
            {
                return;
            }

            _profile.HeadRevision = outcome.Revision;

            if (outcome.RevisionWritten)
            {
                VersionDescription = "";
            }

            await ReloadAsync();

            if (applied && outcome.ApplyMessage is null)
            {
                OfferActivation();
            }
        }
        finally
        {
            IsSaving = false;
            IsReadOnly = false;
        }
    }

    private void OnRunAdvanced() => _ = OnUiThreadAsync(StampMarks);

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
            await _errorReporter.ShowAsync(exception, $"putting '{_profile.Name}' on '{game.Name}'");
        }
    }

    private void OnLeasesChanged(object? sender, EventArgs e) => _ = OnUiThreadAsync(() =>
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

            if (modal.Result is not ProfileDto source)
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
                _toasts.Show($"Nothing to copy - this profile already holds everything {source.Name} does.");

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
            ? $"Nothing in the left list matches the {asked} {names} you pasted."
            : $"Selected {matched} of the {asked} {names} you pasted.";

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
    /// Makes one of a game's mod folders the only source, for a page opened at that folder - which means
    /// arriving from the drift notice. Called before the page loads, and again if it is already open.
    /// </summary>
    public void ScanTarget(ModTargetRef target)
    {
        var loaded = Sources.FocusOn(ModSourceId.ForTarget(target));

        PinnedFilter = PinnedModFilter.NotInSources;

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
            _ = CommitAsync(Current with { Loaded = loaded }, "Loaded the game's mod folder");
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

    protected override async Task InitAsync()
    {
        // A save of this profile that this page did not start: wait it out read-only, then read what it wrote.
        if (_saveService.Find(_profile.Id) is ProfileSaveRun run)
        {
            IsWaitingForSave = true;
            IsReadOnly = true;

            try
            {
                await run.Completion;
            }
            finally
            {
                IsWaitingForSave = false;
                IsReadOnly = false;
            }
        }

        await ReloadAsync();
    }

    protected override void OnInitFailed(Exception ex)
    {
        if (ex is not OperationCanceledException)
        {
            base.OnInitFailed(ex);
        }
    }

    /// <summary>
    /// Reads the profile from the server and starts a new draft from it: on opening, and after a save.
    /// What is loaded stays loaded.
    /// </summary>
    private async Task ReloadAsync()
    {
        IsLoading = true;

        try
        {
            var modListRead = _dependenciesClient.GetModDependenciesV1Async(_repo.Id, _profile.Id, null, _cancellation.Token);
            var ignoredRead = _profilesClient.GetProfileIgnoredModsV1Async(_repo.Id, _profile.Id, _cancellation.Token);

            var modList = await modListRead;
            var ignored = await ignoredRead;

            await OnUiThreadAsync(() =>
            {
                _basedOn = modList.Revision;
                _markedRun = null;
                _savedDates = modList.Dependencies.ToDictionary(
                    x => ModKey.From(x.ModId),
                    x => new SavedPinDate(ModVersionKey.From(x.ModVersionId), x.Added));

                _history.Reset(Current with
                {
                    Draft = new ProfileDraft(OtherProfilesReader.ToPins(modList), ignored.ModIds.Select(ModKey.From))
                });
            });

            await RecomposeAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static Task OnUiThreadAsync(Action work) => Application.Current.Dispatcher.InvokeAsync(work).Task;

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


    public void Dispose()
    {
        _navigationLock.ReleaseLock(this);
        _notices.Release(_activeProfile);
        _leases.Changed -= OnLeasesChanged;
        _syncService.ModFolderChanged -= OnModFolderChanged;
        _repo.Games.CollectionChanged -= OnGamesChanged;

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


    public class Factory(IServiceProvider serviceProvider)
    {
        /// <param name="scanTarget">A folder to load from the start. Null reads no disk until a source is switched on.</param>
        public ProfileModsEditorPageViewModel Create(Repo repo, ProfileDto profile, ModTargetRef? scanTarget = null)
        {
            var page = ActivatorUtilities.CreateInstance<ProfileModsEditorPageViewModel>(serviceProvider, repo, profile);

            // Here rather than through the constructor: a nullable struct does not survive
            // ActivatorUtilities' positional matching.
            if (scanTarget is ModTargetRef target)
            {
                page.ScanTarget(target);
            }

            return page;
        }
    }
}


