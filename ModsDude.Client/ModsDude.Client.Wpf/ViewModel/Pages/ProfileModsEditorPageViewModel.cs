using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
/// see <see cref="ProfileApplyTargets"/> - and <em>Save only</em> costs a second click through the
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
    private readonly ModImportService _importService;
    private readonly IModDependenciesClient _dependenciesClient;
    private readonly IProfilesClient _profilesClient;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly IDialogService _dialogService;
    private readonly NavigationLockService _navigationLock;
    private readonly LocalInstanceRepository _localInstanceRepository;
    private readonly ProfileApplyService _applyService;
    private readonly InstanceDriftMonitor _driftMonitor;
    private readonly DriftNotificationViewModel _driftNotification;
    private readonly ActiveProfile _activeProfile;

    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>
    /// Set by <em>Save only</em> for exactly one save and cleared as that save reads it. A control the
    /// user could leave switched on would convert a per-save decision into a standing mode, which is
    /// the opposite of what it is for.
    /// </summary>
    private bool _skipApplyOnce;

    /// <summary>Every known version of every known mod, oldest first, per mod.</summary>
    private IReadOnlyDictionary<ModKey, IReadOnlyList<CatalogModVersion>> _versionsByMod =
        new Dictionary<ModKey, IReadOnlyList<CatalogModVersion>>();

    /// <summary>The same set, restricted to what the repo holds - the only thing "newer" may read.</summary>
    private IReadOnlyDictionary<ModKey, IReadOnlyList<CatalogModVersion>> _registered =
        new Dictionary<ModKey, IReadOnlyList<CatalogModVersion>>();

    /// <summary>What the profile held when it was last read from the server. Save diffs against it.</summary>
    private IReadOnlyList<ProfileModPin> _original = [];

    /// <summary>
    /// The revision <see cref="_original"/> was read at, and what a save is based on. Taken from the
    /// same response the list came out of rather than from the profile, because that is the only
    /// form of it that cannot already be stale by the time it is used.
    /// </summary>
    private int _basedOn;

    private IReadOnlyList<ModListItemViewModel> _available = [];
    private ProfileModUpdatePlan _updates = ProfileModUpdatePlan.Empty;

    /// <summary>What the left list has to hide, kept as a set because it is asked once per row.</summary>
    private HashSet<ModKey> _pinnedIds = [];

    /// <summary>
    /// Mods the profile still holds on the server and this draft does not - taken out, and waiting
    /// for a save to write that. They are back on the left, which is where they would be if they had
    /// never been in the profile at all, so the sort is what tells the two apart.
    /// </summary>
    private HashSet<ModKey> _pendingRemovals = [];

    /// <summary>
    /// Tracked rather than re-derived from the repo, so an instance dropped from its list is still
    /// unsubscribed from.
    /// </summary>
    private readonly List<LocalInstance> _watchedInstances = [];

    /// <summary>
    /// Set while the list is being rebuilt wholesale - from the server, or by a bulk add. Every add
    /// into <see cref="Pinned"/> would otherwise recount against a draft that is only half written.
    /// </summary>
    private bool _publishing;


    public ProfileModsEditorPageViewModel(
        Repo repo,
        ProfileDto profile,
        ModCatalog.Factory catalogFactory,
        ModListItemViewModel.Factory itemFactory,
        ModImportService importService,
        IModDependenciesClient dependenciesClient,
        IProfilesClient profilesClient,
        IModalService modalService,
        IErrorReporter errorReporter,
        IDialogService dialogService,
        NavigationLockService navigationLock,
        LocalInstanceRepository localInstanceRepository,
        ProfileApplyService applyService,
        InstanceDriftMonitor driftMonitor,
        DriftNotificationViewModel driftNotification)
    {
        _repo = repo;
        _profile = profile;
        _itemFactory = itemFactory;
        _importService = importService;
        _dependenciesClient = dependenciesClient;
        _profilesClient = profilesClient;
        _modalService = modalService;
        _errorReporter = errorReporter;
        _dialogService = dialogService;
        _navigationLock = navigationLock;
        _localInstanceRepository = localInstanceRepository;
        _applyService = applyService;
        _driftMonitor = driftMonitor;
        _driftNotification = driftNotification;
        _activeProfile = new ActiveProfile(repo.Id, profile.Id);

        // The page owns the catalog and disposes it, so the per-source scan cache lives exactly as
        // long as the checkboxes that recompose from it.
        _catalog = catalogFactory.Create(repo);

        ProfileName = profile.Name;

        PinnedView = (ListCollectionView)CollectionViewSource.GetDefaultView(Pinned);
        PinnedView.CustomSort = Comparer<ProfileModRowViewModel>.Create(ComparePinned);

        // One box over both lists, as on the repo mods page. A mod is only ever on one side, so a
        // search that reached only the left one answered half the question somebody was asking -
        // and the half it answered was the side they were least likely to be looking for.
        PinnedView.Filter = x => x is ProfileModRowViewModel row && row.Matches(SearchText);

        Pinned.CollectionChanged += (_, _) => OnPinnedChanged();

        _repo.LocalInstances.CollectionChanged += OnLocalInstancesChanged;
        RefreshApplyTargets();

        // The one place the app-level drift notice is suppressed: somebody already looking at the
        // drifted profile's mod list does not need to be told about it.
        _driftNotification.SuppressFor(_activeProfile);
    }


    public string ProfileName { get; }

    public ObservableCollection<ModSourceViewModel> Sources { get; } = [];

    /// <summary>The profile's pinned mods, one per mod - the domain allows no more than that.</summary>
    public ObservableCollection<ProfileModRowViewModel> Pinned { get; } = [];

    public ListCollectionView PinnedView { get; }

    /// <summary>What is not in the profile yet, registered or merely on disk.</summary>
    [ObservableProperty]
    private ICollectionView? _availableView;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinnedCountText))]
    [NotifyPropertyChangedFor(nameof(HasPinnedMods))]
    private int _pinnedCount;

    /// <summary>How many of those the search is showing. Equal to PinnedCount when nothing is typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinnedCountText))]
    [NotifyPropertyChangedFor(nameof(HasVisiblePinnedMods))]
    private int _pinnedVisibleCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingText))]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    private int _pendingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateCountText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllUpdatesCommand))]
    private int _updateCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkippedText))]
    [NotifyPropertyChangedFor(nameof(HasSkippedUpdates))]
    [NotifyPropertyChangedFor(nameof(ApplyUpdatesText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllUpdatesCommand))]
    private int _skippedUpdateCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplyUpdatesText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllUpdatesCommand))]
    private int _applicableUpdateCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveOnlyCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    private bool _hasUnsavedChanges;

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
    [NotifyCanExecuteChangedFor(nameof(ApplyAllUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddAllShownNewCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreRemovedCommand))]
    private bool _isSaving;

    /// <summary>What the last save did, kept until something changes again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveSummary))]
    private string? _saveSummary;


    /// <summary>
    /// The instances this save re-applies to: read-only, and only rendered from two upwards. With one
    /// the word "instance" never appears at all, which is the common case for most games.
    /// </summary>
    public ObservableCollection<LocalInstance> ApplyTargets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveActionText))]
    [NotifyPropertyChangedFor(nameof(HasApplyTargets))]
    [NotifyPropertyChangedFor(nameof(ShowApplyTargetList))]
    [NotifyCanExecuteChangedFor(nameof(SaveOnlyCommand))]
    private int _applyTargetCount;

    /// <summary>What the last save's apply did, per instance.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasApplyStatus))]
    private string? _applyStatus;

    /// <summary>
    /// Offered after a save that had nothing to apply to, rather than folded into the save itself:
    /// activation is a mode change with a chosen target, which is a different operation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivationOffer))]
    private string? _activationOffer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActivationChoice))]
    [NotifyCanExecuteChangedFor(nameof(AcceptActivationOfferCommand))]
    private LocalInstance? _activationCandidate;

    public ObservableCollection<LocalInstance> ActivationCandidates { get; } = [];

    public bool HasActivationOffer => ActivationOffer is not null;
    public bool HasActivationChoice => ActivationCandidates.Count > 1;

    public bool HasApplyTargets => ApplyTargetCount > 0;
    public bool ShowApplyTargetList => ApplyTargetCount > 1;
    public bool HasApplyStatus => ApplyStatus is not null;

    public string SaveActionText => ProfileApplyTargets.DescribeSaveAction(ApplyTargetCount);

    /// <summary>
    /// Worded with the consequence rather than as a caution. Someone reading only the label has to be
    /// able to tell what it leaves behind.
    /// </summary>
    public string SaveOnlyDescription =>
        "Saves the profile but leaves your installed mods untouched. Your locked mods stay at the versions " +
        "the game updated them to. Only if you know exactly what you are doing.";

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
    public bool HasSaveSummary => SaveSummary is not null;

    public string AvailableCountText => Describe(AvailableCount, AvailableTotal);

    /// <summary>
    /// Says why the top of the left list is not alphabetical. Worded as what a save will do, because
    /// until then the profile still holds them.
    /// </summary>
    public string RemovalText => RemovalCount == 1
        ? "1 taken out"
        : $"{RemovalCount} taken out";

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
    /// Reads at zero as well as above it. The section it heads is always on screen, so "none" is an
    /// answer it has to be able to give - and it is the answer someone who came here to check for
    /// updates was looking for.
    /// </summary>
    public string UpdateCountText => UpdateCount switch
    {
        0 => "No updates available",
        1 => "1 update available",
        _ => $"{UpdateCount} updates available"
    };

    public string ApplyUpdatesText => ApplicableUpdateCount switch
    {
        0 => "Update all",
        1 => "Update 1 mod",
        _ => $"Update {ApplicableUpdateCount} mods"
    };

    /// <summary>
    /// A link rather than a footnote: it opens the same dialog the per-row change opens, reached
    /// deliberately instead of fired at every save.
    /// </summary>
    public string SkippedText => SkippedUpdateCount == 1 ? "1 locked, skipped" : $"{SkippedUpdateCount} locked, skipped";


    #region Moving mods between the lists

    [RelayCommand]
    private void Add(ModListItemViewModel? row)
    {
        if (row is null || Pinned.Any(x => x.ModId == row.Mod.ModId))
        {
            return;
        }

        var versions = _versionsByMod.TryGetValue(row.Mod.ModId, out var known) ? known : [row.Mod];

        Pinned.Add(CreatePinnedRow(versions, row.Mod, lockedByProfile: false));
    }

    /// <summary>
    /// Every mod the left list is showing that this draft has not just taken out, so a search is how
    /// a subset is picked. Putting a removal back is <see cref="RestoreRemoved"/>: it is an undo, and
    /// it has a version and a lock to restore rather than a default to pick.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddAllShownNew))]
    private void AddAllShownNew()
    {
        InBulk(() =>
        {
            foreach (var row in _available.Where(x => Passes(x) && IsPendingRemoval(x) is false).ToList())
            {
                Add(row);
            }
        });
    }

    private bool CanAddAllShownNew() => NewCount > 0 && IsSaving is false;

    /// <summary>
    /// Puts back everything this draft has taken out, at the version and lock the profile still holds
    /// on the server - which is what makes it an undo rather than a re-add. Not limited to what the
    /// search is showing: it undoes the removals, and a removal the user cannot currently see is
    /// still one of them.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreRemoved))]
    private void RestoreRemoved()
    {
        InBulk(() =>
        {
            foreach (var pin in _original.Where(x => _pendingRemovals.Contains(x.ModId)).ToList())
            {
                var versions = _versionsByMod.GetValueOrDefault(pin.ModId, []);
                var selected = versions.FirstOrDefault(x => x.VersionId == pin.VersionId);

                if (selected is null)
                {
                    // The same stand-in a load builds for a pin this catalog has not heard of, and
                    // for the same reason: the row has to be there to be removable again.
                    selected = Placeholder(pin.ModId, pin.VersionId);
                    versions = [selected, .. versions];
                }

                Pinned.Add(CreatePinnedRow(versions, selected, pin.Lock.ByProfile));
            }
        });
    }

    private bool CanRestoreRemoved() => RemovalCount > 0 && IsSaving is false;

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

    #endregion


    #region Updates

    /// <summary>
    /// Applies every update the profile is allowed to take, and says how many it left. Locked mods
    /// are not candidates rather than candidates the save asks about, so the save that follows cannot
    /// contain an unintended version change and needs no prompt at all.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyAllUpdates))]
    private void ApplyAllUpdates()
    {
        foreach (var update in _updates.Available)
        {
            FindPinned(update.ModId)?.SetVersion(update.To);
        }
    }

    private bool CanApplyAllUpdates() => ApplicableUpdateCount > 0 && IsSaving is false;

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
    /// Carries why the mod is locked, because that is the part that decides the answer - and words
    /// the profile lock as being about this profile, which is the only scope it has.
    /// </summary>
    private async Task<bool> ConfirmLockedVersionChangeAsync(ProfileModRowViewModel row, ProfileModVersionOption target)
    {
        var reason = row.Lock.Source switch
        {
            ProfileModLockSource.Adapter =>
                "The game adapter reads it as version-sensitive - a map, typically - so changing its version "
                    + "partway through a save can corrupt that save, and the damage tends to show up long after.",
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
                + $"Move this profile to {target.Version.VersionId}?",
            IconKind.Warning,
            "Change the version",
            "Leave it alone");

        await _modalService.Show(confirmation);

        return confirmation.Result;
    }

    #endregion


    #region Saving

    /// <summary>
    /// Imports whatever is pending, then writes the dependencies, then re-applies - in that order,
    /// because a mod is never registered before its file is in storage and a dependency can only
    /// name a registered version.
    /// </summary>
    /// <remarks>
    /// <b>An import that does not fully succeed stops the save.</b> The steps after it are written
    /// against the mods the repo now holds, so carrying on with a short list quietly turns "these
    /// files failed to upload" into a profile that never mentions them and an apply that treats them
    /// as unrecognised - which sends the very files the user was importing to the Recycle Bin, one
    /// confirmation click away. Nothing downstream can tell that apart from a folder full of junk,
    /// so the only place it can be caught is here, before anything is written.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSave), IncludeCancelCommand = true)]
    private async Task SaveChanges(CancellationToken cancellationToken)
    {
        // Read and cleared here, so the decision cannot outlive the save that carried it.
        var apply = _skipApplyOnce is false;
        _skipApplyOnce = false;

        IsSaving = true;
        SaveSummary = null;
        ApplyStatus = null;
        ActivationOffer = null;

        try
        {
            var import = await ImportPendingAsync(cancellationToken);

            var unfinished = Pinned
                .Count(x => x.IsPending && import.Imported.Contains(x.SelectedVersion.Version.Identity) is false);

            if (unfinished > 0)
            {
                await StopAtFailedImportAsync(unfinished, import.Problems);

                return;
            }

            var desired = Pinned
                .Select(x => x.Pin)
                .ToList();

            var changes = ProfileModListDiff.Compute(_original, desired);

            if (await SaveRevisionAsync(desired, cancellationToken) is false)
            {
                return;
            }

            // What the profile holds now, so that anything left over is the only thing still unsaved.
            _original = desired;

            await ReloadAsync();

            SaveSummary = Describe(changes);

            if (apply)
            {
                await ApplyToTargetsAsync(cancellationToken);
            }
            else
            {
                ApplyStatus = "Saved without applying. Your installed mods are untouched.";
            }
        }
        catch (OperationCanceledException)
        {
            SaveSummary = "Save stopped. Anything already registered stayed registered.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSave() => HasUnsavedChanges && IsSaving is false;

    /// <summary>
    /// Leaves the save where the import stopped it: nothing written to the profile, nothing applied,
    /// every row that did not make it still pending and marked, and the dialog that says why.
    /// </summary>
    /// <remarks>
    /// Deliberately not reloaded, and the original list deliberately not moved on. A reload rebuilds
    /// both lists from the server, and the rows that could not be imported are not on the server -
    /// they would vanish from the profile without the user being told which ones, having just been
    /// told that something went wrong. Leaving the draft where it is also keeps it unsaved, so Save
    /// stays enabled and pressing it again once the cause is fixed is the whole recovery path.
    /// </remarks>
    private async Task StopAtFailedImportAsync(int unfinished, ErrorDialogViewModel? problems)
    {
        Recount();

        // Now that every row carries its outcome, and only now: what could not be imported comes to
        // the top, where the dialog's list can be matched against it.
        PinnedView.Refresh();

        // One line, because the dialog carries the reasons and this is only what is left on the page
        // once it has been dismissed.
        SaveSummary = $"{unfinished} could not be imported, so nothing was saved.";

        if (problems is not null)
        {
            await _modalService.Show(problems);
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

    private bool CanSaveOnly() => CanSave() && HasApplyTargets;

    /// <summary>
    /// Re-applies to the derived targets. An instance that cannot be applied to right now - a
    /// dedicated server mid-session, a folder a running game holds - is reported and left drifted,
    /// which the app-level notice already covers. That is a "not now", not a "not this one".
    /// </summary>
    private async Task ApplyToTargetsAsync(CancellationToken cancellationToken)
    {
        if (ApplyTargets.Count == 0)
        {
            OfferActivation();

            return;
        }

        var messages = new List<string>();

        foreach (var instance in ApplyTargets.ToList())
        {
            ApplyStatus = $"Applying to '{instance.Name}'...";

            var outcome = await _applyService.ApplyAsync(
                _repo,
                instance,
                _profile.Id,
                _profile.Name,
                confirmPlan: false,
                progress: null,
                cancellationToken);

            messages.Add(outcome.Message);
        }

        ApplyStatus = string.Join(" ", messages);

        await _driftMonitor.CheckAsync();
    }

    /// <summary>
    /// The onboarding case: a profile nothing is using yet. Naming the instance because here that
    /// genuinely is a choice, and offered afterwards rather than folded into the save.
    /// </summary>
    private void OfferActivation()
    {
        ActivationCandidates.Clear();

        foreach (var instance in _repo.LocalInstances)
        {
            ActivationCandidates.Add(instance);
        }

        OnPropertyChanged(nameof(HasActivationChoice));

        ActivationCandidate = ActivationCandidates.FirstOrDefault();

        ActivationOffer = ActivationCandidate is LocalInstance candidate
            ? ActivationCandidates.Count == 1
                ? $"No instance is using this profile. Use it on '{candidate.Name}'?"
                : "No instance is using this profile. Use it on one of these?"
            : null;
    }

    [RelayCommand(CanExecute = nameof(CanAcceptActivationOffer))]
    private async Task AcceptActivationOffer(CancellationToken cancellationToken)
    {
        if (ActivationCandidate is not LocalInstance instance)
        {
            return;
        }

        ActivationOffer = null;

        var outcome = await _applyService.ApplyAsync(
            _repo,
            instance,
            _profile.Id,
            _profile.Name,
            // A mode change, not a re-apply: what the previous profile put in that folder comes back
            // out, so the reconciler's plan is the confirmation.
            confirmPlan: true,
            progress: null,
            cancellationToken);

        if (outcome.Status is not ProfileApplyStatus.Declined)
        {
            _localInstanceRepository.SetActiveProfile(instance, _activeProfile);
            RefreshApplyTargets();
        }

        ApplyStatus = outcome.Message;

        await _driftMonitor.CheckAsync();
    }

    private bool CanAcceptActivationOffer() => ActivationCandidate is not null;

    [RelayCommand]
    private void DismissActivationOffer()
    {
        ActivationOffer = null;
    }

    /// <summary>
    /// Uploads and registers the rows that are still only on disk, and reports which of them the repo
    /// now holds - and, where some did not make it, the one dialog that says so.
    /// </summary>
    /// <remarks>
    /// The dialog is built here and shown by the caller: this is where the rows and their names are,
    /// and the caller is where what the failures cost the save is known.
    /// </remarks>
    private async Task<PendingImport> ImportPendingAsync(CancellationToken cancellationToken)
    {
        var pending = Pinned.Where(x => x.IsPending).ToList();

        if (pending.Count == 0)
        {
            return new PendingImport([], null);
        }

        var rows = pending.ToDictionary(x => x.SelectedVersion.Version.Identity, x => x.Item);

        foreach (var item in rows.Values)
        {
            item.ResetImportState();
        }

        var request = new ModImportRequest(
            _repo.Id,
            [.. pending.Select(x => x.SelectedVersion.Version)],
            _repo.Adapter.VersionComparer)
        {
            Progress = new RowProgressReporter(rows),
            ResolveArbitration = ResolveArbitrationAsync
        };

        // The overload that invalidates the catalog afterwards: a partly failed import still
        // registered something, and a catalog that kept claiming otherwise would offer those versions
        // for import all over again.
        var result = await _importService.ImportAsync(_catalog, request, cancellationToken);

        foreach (var item in result.Items)
        {
            if (rows.TryGetValue(item.Identity, out var row))
            {
                row.Apply(item);
            }
        }

        // The profile keeps its draft when an import falls short, so the mods it names are still in
        // the list the user is looking at.
        var problems = ModImportProblems.Build(
            _errorReporter,
            result,
            id => rows.TryGetValue(id, out var row) ? row.Name : id.ModId.Value,
            "Nothing was saved.");

        return new PendingImport([.. result.Succeeded.Select(x => x.Identity)], problems);
    }

    /// <param name="Imported">What the repo holds now, which is what the save is allowed to pin.</param>
    /// <param name="Problems">The dialog for what did not make it, or null when everything did.</param>
    private sealed record PendingImport(
        HashSet<ModVersionIdentity> Imported,
        ErrorDialogViewModel? Problems);

    /// <summary>
    /// One dialog for the whole save, and only for the mods whose version ordering the comparer could
    /// not settle. Everything it settled is already registering by the time this is asked.
    /// </summary>
    private async Task<IReadOnlyDictionary<ModKey, IReadOnlyList<ModVersionKey>>?> ResolveArbitrationAsync(
        IReadOnlyList<ModVersionArbitrationItem> items,
        CancellationToken cancellationToken)
    {
        // The import runs off the UI thread, and everything from here down is view models a
        // dispatcher-bound modal is about to render.
        return await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var modal = new ModVersionArbitrationModalViewModel(items);

            await _modalService.Show(modal);

            return modal.Result;
        }).Task.Unwrap();
    }

    /// <summary>
    /// Writes the whole mod list as a new revision. Returns false when nothing was saved, having
    /// already said why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One request, carrying every pin. The profile's writes are last and on their own, after the
    /// import - a dependency can only name a version the repo already holds.
    /// </para>
    /// <para>
    /// This used to be a delete, an upgrade batch and an add or update per changed mod, and it was
    /// the diff that made that bearable. The diff is still computed, but only to describe the save
    /// afterwards: what goes over the wire is the list itself, because a revision is a snapshot and
    /// the server has to record exactly what the page shows.
    /// </para>
    /// </remarks>
    private async Task<bool> SaveRevisionAsync(IReadOnlyList<ProfileModPin> desired, CancellationToken cancellationToken)
    {
        var request = new SaveProfileRevisionRequest
        {
            BasedOn = _basedOn,
            Label = string.IsNullOrWhiteSpace(VersionDescription) ? null : VersionDescription.Trim(),
            Mods = [.. desired.Select(x => new ProfileModPinRequest
            {
                ModId = x.ModId.Value,
                VersionId = x.VersionId.Value,
                Locked = x.Lock.ByProfile
            })]
        };

        try
        {
            var saved = await _profilesClient.SaveProfileRevisionV1Async(_repo.Id, _profile.Id, request, cancellationToken);

            _basedOn = saved.Number;
            _profile.HeadRevision = saved.Number;

            // It described the save that just happened, not the next one. Left in place it would be
            // carried onto an unrelated edit ten minutes later, which is how a history fills with
            // labels that are quietly wrong.
            VersionDescription = "";

            return true;
        }
        catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.ProfileRevisionStale)
        {
            return await ResolveStaleSaveAsync(desired, cancellationToken);
        }
    }

    /// <summary>
    /// Somebody else saved this profile while this list was open. The choice is theirs, and both
    /// answers are safe: what is on the server is a revision either way, so saving over it does not
    /// destroy it - it can be restored from the history.
    /// </summary>
    private async Task<bool> ResolveStaleSaveAsync(IReadOnlyList<ProfileModPin> desired, CancellationToken cancellationToken)
    {
        var confirmation = new ConfirmationDialogViewModel(
            "Somebody else saved this profile",
            "Your list was built from an older revision. Saving anyway records yours as the newest one - theirs stays in the history and can be restored. Loading theirs discards what you have here.",
            IconKind.Warning,
            "Save mine anyway",
            "Load theirs");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            SaveSummary = "Loaded the newer list. Nothing of yours was saved.";

            await ReloadAsync();

            return false;
        }

        // Re-read only the number, so the retry is based on what the server is actually on rather
        // than on what the refusal happened to mention.
        var current = await _dependenciesClient.GetModDependenciesV1Async(_repo.Id, _profile.Id, null, cancellationToken);

        _basedOn = current.Revision;

        return await SaveRevisionAsync(desired, cancellationToken);
    }

    private static string Describe(ProfileModListChanges changes)
    {
        if (changes.IsEmpty)
        {
            return "Nothing to save.";
        }

        var parts = new List<string>();

        if (changes.Added.Count > 0)
        {
            parts.Add($"{changes.Added.Count} added");
        }

        if (changes.Changed.Count > 0)
        {
            parts.Add($"{changes.Changed.Count} changed");
        }

        if (changes.Removed.Count > 0)
        {
            parts.Add($"{changes.Removed.Count} removed");
        }

        return string.Join(" · ", parts);
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

    private bool CanDiscard() => HasUnsavedChanges && IsSaving is false;

    #endregion


    #region Sources

    [RelayCommand]
    private async Task Refresh()
        => await ReloadAsync();

    [RelayCommand]
    private async Task RescanAll()
    {
        _catalog.RescanAll();

        await ReloadAsync();
    }

    /// <summary>
    /// Adds a folder for this session only. Someone building a profile out of a USB stick should not
    /// have that folder haunting the list for months, so nothing about it is written to disk.
    /// </summary>
    [RelayCommand]
    private async Task AddSource()
    {
        if (_dialogService.PickFolder(null) is not string path)
        {
            return;
        }

        _catalog.AddAdHocSource(path);

        await ReloadAsync();
    }

    [RelayCommand]
    private async Task RemoveSource(ModSourceViewModel? source)
    {
        if (source is null || source.IsAdHoc is false)
        {
            return;
        }

        _catalog.RemoveAdHocSource(source.Source.Id);

        await ReloadAsync();
    }

    [RelayCommand]
    private void ClearSearch()
        => SearchText = string.Empty;

    #endregion


    /// <summary>
    /// Switches on the mod folder of one instance, for a page opened <i>at</i> that folder rather
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
    public void ScanInstance(Guid instanceId)
    {
        _catalog.SetEnabled(ModSourceId.ForInstance(instanceId), true);

        // Only reloads where the page is already up; during construction there is nothing to reload
        // and the initial load reads the flag on its way through.
        if (IsLoading is false)
        {
            _ = ReloadAsync();
        }
    }

    public void Dispose()
    {
        _navigationLock.ReleaseLock(this);
        _driftNotification.Release(_activeProfile);

        _repo.LocalInstances.CollectionChanged -= OnLocalInstancesChanged;

        foreach (var instance in _watchedInstances)
        {
            instance.PropertyChanged -= OnInstanceChanged;
        }

        _watchedInstances.Clear();

        // Deliberately not disposed: the wait may still be inside the token's registration, and
        // disposing a source out from under that is not safe. Nothing here holds a wait handle.
        _cancellation.Cancel();
        _catalog.Dispose();
    }


    private void OnLocalInstancesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshApplyTargets();
    }

    private void OnInstanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalInstance.ActiveProfile))
        {
            RefreshApplyTargets();
        }
    }

    /// <summary>
    /// Derived from the instances' own standing intent, every time it could have moved. The count is
    /// what the primary button says, so being a step behind would mislabel it.
    /// </summary>
    private void RefreshApplyTargets()
    {
        foreach (var instance in _watchedInstances)
        {
            instance.PropertyChanged -= OnInstanceChanged;
        }

        _watchedInstances.Clear();

        foreach (var instance in _repo.LocalInstances)
        {
            instance.PropertyChanged += OnInstanceChanged;
            _watchedInstances.Add(instance);
        }

        ApplyTargets.Clear();

        foreach (var instance in _localInstanceRepository.GetInstancesUsing(_activeProfile))
        {
            ApplyTargets.Add(instance);
        }

        ApplyTargetCount = ApplyTargets.Count;
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

    protected override Task InitAsync()
        => LoadAsync();


    private async Task ReloadAsync()
    {
        IsLoading = true;

        try
        {
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-reload.
        }
        finally
        {
            // Publish clears this on the way through; the finally is for the paths that never reach
            // it, so a failed reload does not leave the list claiming to still be reading.
            IsLoading = false;
        }
    }

    private async Task LoadAsync()
    {
        var modList = await _dependenciesClient.GetModDependenciesV1Async(_repo.Id, _profile.Id, null, _cancellation.Token);
        var snapshot = await _catalog.GetAsync(_cancellation.Token);

        // Everything from here down is WPF-facing, and this may well have arrived on a thread-pool
        // thread.
        await Application.Current.Dispatcher.InvokeAsync(() => Publish(snapshot, modList));
    }

    private void Publish(ModCatalogSnapshot snapshot, GetModDependenciesResponse modList)
    {
        _publishing = true;

        try
        {
            // Read from the response that carried the list, so the two cannot disagree about which
            // revision this page is editing.
            _basedOn = modList.Revision;

            var dependencies = modList.Dependencies;
            Sources.Clear();

            foreach (var status in snapshot.Sources)
            {
                Sources.Add(new ModSourceViewModel(status, OnSourceEnabledChanged));
            }

            _versionsByMod = OrderVersions(snapshot.Versions);
            _registered = ProfileModUpdates.Registered(snapshot.Versions);

            foreach (var row in Pinned)
            {
                row.PropertyChanged -= OnPinnedRowChanged;
            }

            Pinned.Clear();

            foreach (var dependency in dependencies)
            {
                var modId = ModKey.From(dependency.ModId);
                var versionId = ModVersionKey.From(dependency.ModVersionId);
                var versions = _versionsByMod.GetValueOrDefault(modId, []);
                var selected = versions.FirstOrDefault(x => x.VersionId == versionId);

                if (selected is null)
                {
                    // This catalog has not heard of the version the profile names - which is a fact
                    // about this client, not about the repo. It stays in the row's selector, at the
                    // front so it cannot read as the newest, because a row that silently vanished
                    // would leave the profile pinned to it with no way to say so.
                    selected = Placeholder(modId, versionId);
                    versions = [selected, .. versions];
                }

                Pinned.Add(CreatePinnedRow(versions, selected, dependency.Locked));
            }

            _original = [.. Pinned.Select(x => x.Pin)];

            // With a single source every row would name the same one, which is just noise.
            var showSources = snapshot.Sources.Count(x => x.IsEnabled) > 1;

            _available = [.. _versionsByMod.Values
                // The newest known version stands for the mod on the left. Picking a different one
                // is a decision that belongs to the row it becomes on the right.
                .Select(x => x[^1])
                .OrderBy(x => x.Name, NaturalOrder.Comparer)
                .Select(x => CreateAvailableRow(x, showSources))];

            // Rebuilt rather than refreshed, because the list behind it is replaced wholesale -
            // adding a couple of thousand rows to a bound collection one at a time is a couple of
            // thousand layout passes.
            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(_available);
            view.Filter = x => x is ModListItemViewModel mod && Passes(mod);
            view.CustomSort = Comparer<ModListItemViewModel>.Create(CompareAvailable);

            AvailableView = view;
            IsLoading = false;
        }
        finally
        {
            _publishing = false;
        }

        Recount();
    }

    /// <summary>
    /// Every version of a mod in one order, so the selector can offer what is on disk alongside what
    /// the repo holds.
    /// </summary>
    /// <remarks>
    /// The repo's own order is handed in as settled and never re-derived: it was arbitrated once and
    /// stored server-side, and clients on different adapter versions recomputing it would disagree
    /// about what an update is. Only the unregistered versions are placed here, which is the one part
    /// the repo has no answer for yet.
    /// </remarks>
    private IReadOnlyDictionary<ModKey, IReadOnlyList<CatalogModVersion>> OrderVersions(
        IReadOnlyList<CatalogModVersion> versions)
    {
        var result = new Dictionary<ModKey, IReadOnlyList<CatalogModVersion>>();

        foreach (var group in versions.GroupBy(x => x.ModId))
        {
            // The catalog deduplicates on (ModId, VersionId), so this cannot collide.
            var byVersion = group.ToDictionary(x => x.VersionId);

            var settled = group
                .Where(x => x.SequenceNumber is not null)
                .OrderBy(x => x.SequenceNumber)
                .Select(x => x.VersionId)
                .ToList();

            var ordering = ModVersionPartialOrder.Derive(
                [.. byVersion.Keys],
                _repo.Adapter.VersionComparer,
                settled);

            result[group.Key] = [.. ordering.Order.Select(x => byVersion[x])];
        }

        return result;
    }

    private ModListItemViewModel CreateAvailableRow(CatalogModVersion version, bool showSources)
    {
        var item = _itemFactory.Create(_repo.Id, version);

        item.Status = version.GetImportStatus();
        item.IsSelectable = false;

        if (showSources && version.FoundIn.Count > 0)
        {
            item.Sources = string.Join(", ", version.FoundIn.Select(source => source.Source.Name));
        }

        return item;
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
            ConfirmLockedVersionChangeAsync);

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

    private bool Passes(ModListItemViewModel mod)
        => mod.Matches(SearchText) && _pinnedIds.Contains(mod.Mod.ModId) is false;

    /// <summary>
    /// The left list's order: what this draft has taken out of the profile first, then alphabetical.
    /// A removed mod looks exactly like one that was never in the profile, and the only thing that
    /// can say otherwise is where it sits.
    /// </summary>
    private int CompareAvailable(ModListItemViewModel left, ModListItemViewModel right)
    {
        var byRemoval = IsPendingRemoval(right).CompareTo(IsPendingRemoval(left));

        return byRemoval != 0
            ? byRemoval
            : NaturalOrder.Compare(left.Name, right.Name);
    }

    private bool IsPendingRemoval(ModListItemViewModel row)
        => _pendingRemovals.Contains(row.Mod.ModId);

    /// <summary>
    /// The right list's order: whatever wants an answer first, then alphabetical. The top of the list
    /// is the part anyone reads after a save, and a mod that could not be imported buried at "S" is
    /// one nobody sees.
    /// </summary>
    private static int ComparePinned(ProfileModRowViewModel left, ProfileModRowViewModel right)
    {
        var byRank = Rank(left).CompareTo(Rank(right));

        return byRank != 0
            ? byRank
            : NaturalOrder.Compare(left.Name, right.Name);
    }

    /// <summary>
    /// How near the top a pinned row belongs. Read when the list is sorted rather than as it changes:
    /// rows reshuffling mid-import would move the list under the pointer that is watching it, so a
    /// save that stops re-sorts once, at the end.
    /// </summary>
    private static int Rank(ProfileModRowViewModel row) => row.Item.ImportState switch
    {
        ModImportRowState.Failed => 0,
        ModImportRowState.Skipped => 1,
        ModImportRowState.Running => 2,
        ModImportRowState.Succeeded => 4,
        _ => row.IsPending ? 3 : 5
    };


    private void OnSourceEnabledChanged(ModSourceViewModel source, bool enabled)
    {
        _catalog.SetEnabled(source.Source, enabled);

        // Recomposes from the scans already in memory, so this is instant for a source that has been
        // read once - which is the whole reason the catalog caches per source.
        RefreshCommand.Execute(null);
    }

    private void OnPinnedRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only the two things a row can change about the profile. Everything else it raises - its
        // nested list row, the lock wording, the update marker this very method sets - either says
        // nothing about what is pinned or would recount from inside the recount that set it.
        if (e.PropertyName is nameof(ProfileModRowViewModel.SelectedVersion)
            or nameof(ProfileModRowViewModel.LockedByProfile))
        {
            Recount();
        }
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
        AvailableView?.Refresh();
        PinnedView.Refresh();

        RecountAvailable();
        RecountPinnedVisible();
    }

    /// <summary>
    /// What the left list is showing, and how much of that the profile has never held - the two the
    /// bulk buttons are counted against, and they part company as soon as something is taken out.
    /// </summary>
    private void RecountAvailable()
    {
        AvailableTotal = _available.Count(x => _pinnedIds.Contains(x.Mod.ModId) is false);
        AvailableCount = _available.Count(Passes);
        NewCount = _available.Count(x => Passes(x) && IsPendingRemoval(x) is false);
    }

    /// <summary>How many of the profile's mods the search is showing. The total is PinnedCount.</summary>
    private void RecountPinnedVisible()
    {
        PinnedVisibleCount = Pinned.Count(x => x.Matches(SearchText));
    }

    private void Recount()
    {
        _pinnedIds = [.. Pinned.Select(x => x.ModId)];
        _pendingRemovals = [.. _original.Select(x => x.ModId).Where(x => _pinnedIds.Contains(x) is false)];

        // The chip that says why a row is at the top of the left list, and the counterpart of the
        // pending-import chip on the right. Marked here rather than when the row is built, because
        // it is the draft that decides it and the draft changes under the same rows.
        foreach (var row in _available)
        {
            row.Status = _pendingRemovals.Contains(row.Mod.ModId)
                ? ModDisplayStatus.PendingRemoval
                : row.Mod.GetImportStatus();
        }

        PinnedCount = Pinned.Count;
        RecountPinnedVisible();
        RemovalCount = _pendingRemovals.Count;
        PendingCount = Pinned.Count(x => x.IsPending);

        // The left list hides what the right one holds, so it re-filters whenever that changes -
        // which is also what keeps a mod off both sides at once.
        AvailableView?.Refresh();
        RecountAvailable();

        _updates = ProfileModUpdates.Plan(Pinned.Select(x => x.Pin), _registered);

        var byMod = _updates.Available.Concat(_updates.Skipped).ToDictionary(x => x.ModId);

        foreach (var row in Pinned)
        {
            row.UpdateTo = byMod.TryGetValue(row.ModId, out var update) ? update.To : null;
        }

        UpdateCount = _updates.Count;
        ApplicableUpdateCount = _updates.Available.Count;
        SkippedUpdateCount = _updates.Skipped.Count;

        HasUnsavedChanges = ProfileModListDiff.Compute(_original, Pinned.Select(x => x.Pin)).IsEmpty is false;

        if (HasUnsavedChanges)
        {
            SaveSummary = null;
            _navigationLock.AcquireLock(this);
        }
        else
        {
            _navigationLock.ReleaseLock(this);
        }
    }


    /// <summary>
    /// Per row, not per save: an import of a few hundred mods needs to say which of them is moving.
    /// </summary>
    /// <remarks>
    /// Byte counts arrive thousands of times per file, on whatever thread is doing the upload, so
    /// anything finer than a whole percent is redraw nobody can see. WPF marshals the property
    /// changes themselves, which is why this does not dispatch.
    /// </remarks>
    private sealed class RowProgressReporter(IReadOnlyDictionary<ModVersionIdentity, ModListItemViewModel> rows)
        : IProgress<ModImportProgress>
    {
        private readonly ConcurrentDictionary<ModVersionIdentity, int> _lastPercent = new();


        public void Report(ModImportProgress value)
        {
            if (rows.TryGetValue(value.Identity, out var row) is false)
            {
                return;
            }

            if (value.Phase is ModImportPhase.Uploading && row.IsUploading)
            {
                var percent = value.TotalBytes > 0
                    ? (int)(value.BytesTransferred * 100 / value.TotalBytes)
                    : 0;

                if (_lastPercent.TryGetValue(value.Identity, out var last) && last == percent)
                {
                    return;
                }

                _lastPercent[value.Identity] = percent;
            }

            row.Apply(value);
        }
    }


    public class Factory(IServiceProvider serviceProvider)
    {
        /// <param name="scanInstanceId">
        /// An instance whose mod folder should be scanned from the start. Null for an ordinary
        /// navigation, which reads no disk at all until the user ticks a source.
        /// </param>
        public ProfileModsEditorPageViewModel Create(Repo repo, ProfileDto profile, Guid? scanInstanceId = null)
        {
            var page = ActivatorUtilities.CreateInstance<ProfileModsEditorPageViewModel>(serviceProvider, repo, profile);

            // Set before the page is handed back, so it is on by the time TriggerInit reads it. Done
            // here rather than through the constructor because a nullable Guid does not survive
            // ActivatorUtilities' positional matching.
            if (scanInstanceId is Guid instanceId)
            {
                page.ScanInstance(instanceId);
            }

            return page;
        }
    }
}
