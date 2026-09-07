using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.Pages;

/// <summary>
/// The repo's savegames: every save on the left, the selected one's history on the right.
/// </summary>
/// <remarks>
/// <para>
/// <b>One repo-level list with a profile column</b>, not a list per profile. A savegame is keyed
/// <c>(RepoId, Id)</c> and its profile is an attribute rather than a parent, so this is the faithful
/// rendering - and two surfaces showing the same rows under different rules is the thing merging
/// Import into Manage removed.
/// </para>
/// <para>
/// <b>Master-detail, not an accordion.</b> A two-pane history does not fit inside a row, and an
/// expander moves the list under the pointer - which is the arrangement the import list is explicitly
/// ordered to avoid.
/// </para>
/// <para>
/// <b>Readable at Guest, claimable at Member.</b> A guest gets the list, the history and <em>Take a
/// copy</em>, and is never offered check-out: a picker leading to a refusal is worse than one never
/// offered.
/// </para>
/// </remarks>
public partial class RepoSavegamesPageViewModel : PageViewModel, IDisposable
{
    private readonly Repo _repo;
    private readonly ISavegamesClient _savegamesClient;
    private readonly SavegameHeadVersionCache _headVersions;
    private readonly ISavegameService _savegameService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileService _profileService;
    private readonly CurrentUserService _currentUserService;
    private readonly LocalInstanceRepository _localInstanceRepository;
    private readonly ProfileApplyService _applyService;
    private readonly ModSyncService _syncService;
    private readonly InstanceDriftMonitor _driftMonitor;
    private readonly SavegameFlowService _flowService;
    private readonly ShellNavigationService _shellNavigation;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;

    private readonly CancellationTokenSource _pageLifetime = new();
    private readonly CancellationToken _lifetime;

    /// <summary>
    /// Whether a locked pin moved between two revisions of one profile, keyed by the pair. One check-out
    /// dialog and one row chip ask the same question about the same pair, and it costs two reads.
    /// </summary>
    private readonly Dictionary<(Guid ProfileId, int From, int To), bool> _lockedDrift = [];

    private IReadOnlyList<SavegameDto> _fetched = [];
    private string? _currentUserId;


    public RepoSavegamesPageViewModel(
        Repo repo,
        ISavegamesClient savegamesClient,
        ISavegameService savegameService,
        SavegameHeadVersionCache headVersions,
        SavegameBindingStore bindingStore,
        ProfileService profileService,
        CurrentUserService currentUserService,
        LocalInstanceRepository localInstanceRepository,
        ProfileApplyService applyService,
        ModSyncService syncService,
        InstanceDriftMonitor driftMonitor,
        SavegameFlowService flowService,
        ShellNavigationService shellNavigation,
        IModalService modalService,
        IErrorReporter errorReporter)
    {
        _repo = repo;
        _savegamesClient = savegamesClient;
        _savegameService = savegameService;
        _headVersions = headVersions;
        _bindingStore = bindingStore;
        _profileService = profileService;
        _currentUserService = currentUserService;
        _localInstanceRepository = localInstanceRepository;
        _applyService = applyService;
        _syncService = syncService;
        _driftMonitor = driftMonitor;
        _flowService = flowService;
        _shellNavigation = shellNavigation;
        _modalService = modalService;
        _errorReporter = errorReporter;

        // Captured once, so that work still in flight after Dispose reads a cancelled token rather
        // than an ObjectDisposedException off the source it came from.
        _lifetime = _pageLifetime.Token;

        CanCheckOut = repo.MembershipLevel >= RepoMembershipLevel.Member;

        // Admin, like pruning a profile's revisions and for the same reason: it destroys a backup,
        // which is not part of running a repo.
        CanPruneVersions = repo.MembershipLevel >= RepoMembershipLevel.Admin;

        Savegames = [];
        Timeline = [];
    }


    public string RepoName => _repo.Name;

    /// <summary>Whether taking the claim is on offer at all. Reading the list and copying a version is not gated.</summary>
    public bool CanCheckOut { get; }

    /// <summary>Whether deleting a version of a savegame's history is on offer. Admin only.</summary>
    public bool CanPruneVersions { get; }

    public ObservableCollection<SavegameListItemViewModel> Savegames { get; }

    /// <summary>Versions and checkouts as one column, newest first.</summary>
    public ObservableCollection<SavegameTimelineEntryViewModel> Timeline { get; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveSavegameCommand))]
    private SavegameListItemViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    [NotifyCanExecuteChangedFor(nameof(CheckOutVersionCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeCopyVersionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteVersionCommand))]
    private SavegameTimelineEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isLoadingTimeline;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckOutVersionCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeCopyVersionCommand))]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    /// <summary>
    /// Set where the listing was windowed. Nothing pages further yet, and saying so beats a history
    /// that quietly stops.
    /// </summary>
    [ObservableProperty]
    private bool _hasOlder;

    [ObservableProperty]
    private bool _isEmpty;


    public bool HasSelection => Selected is not null;
    public bool HasSelectedEntry => SelectedEntry is not null;
    public bool HasStatus => Status is not null;

    public string SelectedTitle => Selected is null ? "" : $"{Selected.Name} · {Selected.ProfileName}";


    protected override async Task InitAsync()
    {
        // The sidebar loads the repo's profiles, but this page can be the first thing opened after a
        // deep link, and every row needs a profile name and a head revision.
        if (_profileService.Profiles.Any(x => x.RepoId == _repo.Id) is false)
        {
            await _profileService.RefreshProfiles(_repo.Id, _lifetime);
        }

        // Which chip says "You have it" rather than naming somebody. Absorbed: a list that cannot tell
        // whose is whose is still a list, and everything else on the page works.
        try
        {
            _currentUserId = (await _currentUserService.Get(_lifetime)).Id;
        }
        catch (ApiException)
        {
            _currentUserId = null;
        }

        _fetched = [.. await _savegamesClient.GetSavegamesV1Async(_repo.Id, _lifetime)];
    }

    protected override void OnInitCompleted()
    {
        Publish(_fetched);

        IsLoading = false;
    }

    public void Dispose()
    {
        _pageLifetime.Cancel();

        ClearRows();

        _pageLifetime.Dispose();
    }


    [RelayCommand]
    private async Task Refresh()
    {
        await ReloadAsync(Selected?.Id);
    }

    /// <summary>
    /// Checks out the selected entry's version. Where that is not the head it is a restore first -
    /// copied forward as a new version, with nothing in between deleted - which is why there is no
    /// separate restore flow to find.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanActOnEntry))]
    private async Task CheckOutVersion()
    {
        if (Selected is SavegameListItemViewModel row && SelectedEntry?.VersionNumber is int number)
        {
            await StartAsync(row, number, SavegameCheckOutMode.CheckOut);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyEntry))]
    private async Task TakeCopyVersion()
    {
        if (Selected is SavegameListItemViewModel row && SelectedEntry?.VersionNumber is int number)
        {
            await StartAsync(row, number, SavegameCheckOutMode.TakeCopy);
        }
    }

    /// <summary>
    /// Puts the selected savegame in the repo's Archive.
    /// </summary>
    /// <remarks>
    /// The only way a savegame goes away, and deliberately not a delete: what it carries is backups
    /// of somebody's play. It keeps its versions and its claim log - archiving a save somebody is
    /// holding must not quietly release their hold on it.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanArchiveSelected))]
    private async Task ArchiveSavegame()
    {
        if (Selected is not SavegameListItemViewModel row)
        {
            return;
        }

        var confirmation = ConfirmationDialogViewModel.ConfirmArchive(row.Name, "savegame");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _savegamesClient.ArchiveSavegameV1Async(_repo.Id, row.Id, _pageLifetime.Token);

            await ReloadAsync(null);
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "archiving a savegame");
        }
        finally
        {
            IsWorking = false;
        }
    }

    // Member, like publishing and checking in: archiving is reversible and is part of keeping the
    // repo's saves tidy. CanPruneVersions is the Admin one, and gates deleting a version.
    private bool CanArchiveSelected() => CanCheckOut && IsWorking is false && Selected is not null;

    /// <summary>
    /// Deletes the selected version from the savegame's history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Admin only, and never the head.</b> It destroys a backup, which is not part of running a
    /// repo; and the head is what a check-out hands people, so a savegame whose current version is
    /// missing is one nobody can play. The server refuses both, and the button is simply absent
    /// rather than present-and-doomed.
    /// </para>
    /// <para>
    /// <b>The reason this exists is a profile's history.</b> A version pins the profile revision it
    /// was played on, so it is what stops that revision being pruned - and "played on save X version
    /// 3" would be an obstacle somebody could see and never move.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDeleteEntry))]
    private async Task DeleteVersion()
    {
        if (Selected is not SavegameListItemViewModel row || SelectedEntry?.VersionNumber is not int number)
        {
            return;
        }

        var confirmation = new ConfirmationDialogViewModel(
            $"Delete version {number}?",
            $"This copy of '{row.Name}' goes for good. The others stay, and whoever is playing it now "
                + "is unaffected - they hold the current version, which this is not.",
            IconKind.Warning,
            "Delete it",
            "Keep it");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _savegamesClient.DeleteSavegameVersionV1Async(_repo.Id, row.Id, number, _pageLifetime.Token);

            await LoadTimelineAsync(row);
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "deleting a savegame version");
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanDeleteEntry()
        => CanPruneVersions && IsWorking is false && SelectedEntry is { IsVersion: true, IsHead: false };

    private bool CanActOnEntry() => CanCheckOut && IsWorking is false && SelectedEntry is { IsVersion: true };
    private bool CanCopyEntry() => IsWorking is false && SelectedEntry is { IsVersion: true };

    /// <summary>
    /// Into the profile's own history, where two revisions can be compared properly. The comparison
    /// already exists; repeating a cut-down version of it here would be a second answer to maintain.
    /// </summary>
    [RelayCommand]
    private async Task CompareRevisions()
    {
        if (Selected is not SavegameListItemViewModel row)
        {
            return;
        }

        if (await _shellNavigation.GoToProfileHistoryAsync(_repo.Id, row.Savegame.ProfileId) is false)
        {
            Status = $"'{row.ProfileName}' could not be opened from here.";
        }
    }


    partial void OnSelectedChanged(SavegameListItemViewModel? value)
    {
        Timeline.Clear();
        SelectedEntry = null;

        if (value is not null)
        {
            _ = LoadTimelineAsync(value);
        }
    }


    private void Publish(IReadOnlyList<SavegameDto> savegames, Guid? select = null)
    {
        // Every path that renders a savegame list goes through here, which is why the drift check's
        // "somebody took this over and checked in" is fed from this one place rather than from each
        // fetch. It answers nothing for a repo whose list nobody has opened - deliberately, since the
        // alternative is a round trip per held save on every window activation.
        _headVersions.Record(_repo.Id, savegames);

        var wanted = select ?? Selected?.Id;

        ClearRows();

        // Two people called Anton can both hold a save in this repo, and neither of them is the
        // duplicate - so the tag goes on both or on neither, decided over this list.
        var ambiguous = UserDisplay.FindAmbiguous(
            savegames.Select(x => x.Checkout?.User).OfType<UserDto>());

        foreach (var savegame in savegames.OrderBy(x => x.Name, NaturalOrder.Comparer))
        {
            var row = new SavegameListItemViewModel(
                savegame,
                FindProfile(savegame.ProfileId)?.Name ?? "A profile you cannot see",
                _currentUserId,
                CanCheckOut,
                ambiguous.Contains(savegame.Checkout?.User.Id ?? ""));

            row.CheckOutRequested += OnCheckOutRequested;
            row.TakeCopyRequested += OnTakeCopyRequested;

            Savegames.Add(row);
        }

        IsEmpty = Savegames.Count == 0;

        // Assigning this is what loads the timeline, so it happens after the rows exist.
        Selected = Savegames.FirstOrDefault(x => x.Id == wanted) ?? Savegames.FirstOrDefault();

        _ = AnnotateAsync([.. Savegames]);
    }

    private void ClearRows()
    {
        foreach (var row in Savegames)
        {
            row.CheckOutRequested -= OnCheckOutRequested;
            row.TakeCopyRequested -= OnTakeCopyRequested;
        }

        Savegames.Clear();
    }

    private async Task ReloadAsync(Guid? select)
    {
        IsLoading = true;

        try
        {
            var savegames = await _savegamesClient.GetSavegamesV1Async(_repo.Id, _lifetime);

            Publish([.. savegames], select);
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-refresh. There is nothing left to publish to.
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// The two chips that are not facts about the savegame: whether a slot on <em>this</em> machine has
    /// moved, and how far behind the save's revision is. Both are appended as they arrive rather than
    /// holding up a list that is otherwise ready, and both are absorbed on failure - a missing chip
    /// costs a caption, and the row is still correct without it.
    /// </summary>
    private async Task AnnotateAsync(IReadOnlyList<SavegameListItemViewModel> rows)
    {
        try
        {
            await AnnotateRevisionsAsync(rows);
            await AnnotateUnpublishedPlayAsync(rows);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing awaits this, so an escape would go unobserved rather than reaching the shell.
            Status ??= $"Some of this list could not be checked against your machine: {exception.Message}";
        }
    }

    private async Task AnnotateRevisionsAsync(IReadOnlyList<SavegameListItemViewModel> rows)
    {
        foreach (var row in rows)
        {
            _lifetime.ThrowIfCancellationRequested();

            if (row.Savegame.Head is not SavegameVersionDto head ||
                FindProfile(row.Savegame.ProfileId) is not ProfileDto profile)
            {
                continue;
            }

            var behind = profile.HeadRevision - head.ProfileRevision;

            if (behind <= 0)
            {
                continue;
            }

            row.SetRevisionDrift(
                behind,
                await LockedPinMovedAsync(profile.Id, head.ProfileRevision, profile.HeadRevision));
        }
    }

    /// <summary>
    /// Whether any locked pin moved between two revisions. An unlocked mod at a different version is
    /// untidy; a locked map at a different version is a damaged save, and only the second is worth
    /// colouring a chip for.
    /// </summary>
    private async Task<bool> LockedPinMovedAsync(Guid profileId, int from, int to)
    {
        if (_lockedDrift.TryGetValue((profileId, from, to), out var cached))
        {
            return cached;
        }

        bool moved;

        try
        {
            var comparison = await _profileService.CompareRevisions(_repo.Id, profileId, from, to, _lifetime);

            moved = comparison.Changes.Any(x => x.VersionMoved && (x.FromLocked || x.ToLocked || x.Version.Locked));
        }
        catch (ApiException)
        {
            // The count is still true and still worth showing; only the colour is unknown, and the
            // quiet answer is the right one to guess when it is.
            moved = false;
        }

        _lockedDrift[(profileId, from, to)] = moved;

        return moved;
    }

    private async Task AnnotateUnpublishedPlayAsync(IReadOnlyList<SavegameListItemViewModel> rows)
    {
        foreach (var instance in _repo.LocalInstances.ToList())
        {
            foreach (var binding in _bindingStore.GetBindings(instance.Id))
            {
                _lifetime.ThrowIfCancellationRequested();

                if (rows.FirstOrDefault(x => x.Id == binding.SavegameId) is not SavegameListItemViewModel row)
                {
                    continue;
                }

                var availability = await _savegameService.ClassifySlotAsync(
                    instance, new SavegameSlotId(binding.SlotId), _lifetime);

                if (availability is SavegameSlotAvailability.HeldWithUnpublishedPlay)
                {
                    row.SetUnpublishedPlay(true);
                }
            }
        }
    }

    /// <summary>
    /// Versions and checkouts, merged and ordered newest first. Two reads rather than one, because the
    /// server keeps them as two logs on purpose - the checkout rows outlive the blobs, so history can
    /// still say that a version existed and was pruned.
    /// </summary>
    private async Task LoadTimelineAsync(SavegameListItemViewModel row)
    {
        IsLoadingTimeline = true;

        try
        {
            var versions = await _savegamesClient.GetSavegameVersionsV1Async(
                _repo.Id, row.Id, null, null, _lifetime);

            var checkouts = await _savegamesClient.GetSavegameCheckoutsV1Async(
                _repo.Id, row.Id, null, null, _lifetime);

            // The selection can have moved on while this was in flight, in which case this answer is
            // about a savegame nobody is looking at any more.
            if (ReferenceEquals(Selected, row) is false)
            {
                return;
            }

            var entries = versions.Versions
                .Select(x => SavegameTimelineEntryViewModel.ForVersion(x, x.Number == versions.HeadVersion))
                .Concat(checkouts.Checkouts.Select(SavegameTimelineEntryViewModel.ForCheckout))
                .OrderByDescending(x => x.Moment);

            Timeline.Clear();

            foreach (var entry in entries)
            {
                Timeline.Add(entry);
            }

            HasOlder = versions.HasMore || checkouts.HasMore;

            SelectedEntry = Timeline.FirstOrDefault();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Timeline.Clear();
            Status = $"Could not read the history of '{row.Name}': {exception.Message}";
        }
        finally
        {
            IsLoadingTimeline = false;
        }
    }


    private async void OnCheckOutRequested(object? sender, EventArgs e)
    {
        if (sender is SavegameListItemViewModel row)
        {
            await StartAsync(row, row.Savegame.Head?.Number ?? 0, SavegameCheckOutMode.CheckOut);
        }
    }

    private async void OnTakeCopyRequested(object? sender, EventArgs e)
    {
        if (sender is SavegameListItemViewModel row)
        {
            await StartAsync(row, row.Savegame.Head?.Number ?? 0, SavegameCheckOutMode.TakeCopy);
        }
    }

    /// <summary>
    /// The destructive step is local and comes first, the claim is social and wants to be fast, and the
    /// mod question is last because it is the only one that can be deferred. This is that order.
    /// </summary>
    private async Task StartAsync(SavegameListItemViewModel row, int versionNumber, SavegameCheckOutMode mode)
    {
        if (row.Savegame.Head is null || versionNumber <= 0)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Refusal(
                $"'{row.Name}' has no version yet",
                "Nothing has been checked in for this savegame, so there is nothing to write into a slot."));

            return;
        }

        var instances = _repo.LocalInstances.ToList();

        if (instances.Count == 0)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Refusal(
                "No game is connected here",
                $"A savegame has to be written into an installation of the game. Use 'Connect game' in {_repo.Name} first."));

            return;
        }

        IsWorking = true;

        try
        {
            // The instance already following this save's profile is the likeliest target, and it is
            // also the one whose mod folder is already right.
            var preferred = instances.FirstOrDefault(x =>
                x.ActiveProfile == new ActiveProfile(_repo.Id, row.Savegame.ProfileId)) ?? instances[0];

            var context = await BuildContextAsync(row, preferred, mode, _lifetime);

            var modal = new SavegameCheckOutModalViewModel(
                mode,
                row.Name,
                row.ProfileName,
                versionNumber,
                row.Savegame.Head.Number,
                instances,
                context,
                (instance, cancellationToken) => BuildContextAsync(row, instance, mode, cancellationToken));

            await _modalService.Show(modal);

            if (modal.CheckInFirstSavegameId is Guid blocking)
            {
                await CheckInBlockingAsync(modal.SelectedInstance, blocking, row, versionNumber, mode);

                return;
            }

            if (modal.Result is not SavegameCheckOutResult result)
            {
                return;
            }

            await ExecuteAsync(row, versionNumber, mode, result);
        }
        catch (OperationCanceledException)
        {
            // Navigated away. Nothing was written, and there is no page left to say so on.
        }
        catch (Exception exception)
        {
            // The rows raise plain events rather than running commands, so a failure here has no
            // command to carry it to the global handler and has to reach the user itself.
            await _errorReporter.ShowAsync(exception, "checking a savegame out");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// The way out of a refused slot: check the savegame occupying it in, then offer this dialog again
    /// with the slot free. One action rather than a warning, per docs/PLAN.md#slot-safety.
    /// </summary>
    private async Task CheckInBlockingAsync(
        LocalInstance? instance,
        Guid blockingSavegameId,
        SavegameListItemViewModel row,
        int versionNumber,
        SavegameCheckOutMode mode)
    {
        if (instance is null)
        {
            return;
        }

        var blocking = Savegames.FirstOrDefault(x => x.Id == blockingSavegameId);

        var outcome = await _flowService.CheckInAsync(
            instance,
            blockingSavegameId,
            blocking?.Name ?? "that savegame",
            blocking?.Name ?? "the slot",
            _lifetime);

        if (outcome.ReleasedTheSlot is false)
        {
            Status = outcome.WasDeferred
                ? "That savegame was left checked out, so its slot is still taken."
                : "That savegame is still checked out, so its slot is still taken.";

            return;
        }

        await ReloadAsync(row.Id);

        if (Savegames.FirstOrDefault(x => x.Id == row.Id) is SavegameListItemViewModel refreshed)
        {
            await StartAsync(refreshed, versionNumber, mode);
        }
    }

    private async Task ExecuteAsync(
        SavegameListItemViewModel row,
        int versionNumber,
        SavegameCheckOutMode mode,
        SavegameCheckOutResult result)
    {
        if (mode is SavegameCheckOutMode.TakeCopy)
        {
            await _savegameService.TakeCopyAsync(result.Instance, row.Savegame, versionNumber, result.Slot.Id, _lifetime);

            Status = $"Version {versionNumber} of '{row.Name}' is in '{result.Instance.Name}'. Nobody was stopped from playing it, " +
                     "and this machine holds no claim on it - the slot is an ordinary save of your own now.";

            return;
        }

        var savegame = row.Savegame;

        // Restoring copies forward, so an old version becomes the head and the check-out that follows
        // has no stale base to reason about. Nothing in between is deleted.
        if (versionNumber != savegame.Head?.Number)
        {
            await _savegamesClient.RestoreSavegameVersionV1Async(
                _repo.Id, savegame.Id, versionNumber, new RestoreSavegameVersionRequest(), _lifetime);

            var refreshed = await _savegamesClient.GetSavegamesV1Async(_repo.Id, _lifetime);

            savegame = refreshed.FirstOrDefault(x => x.Id == savegame.Id) ?? savegame;
        }

        await _savegameService.CheckOutAsync(result.Instance, savegame, result.Slot.Id, _lifetime);

        Status = $"'{row.Name}' is checked out to you, in '{result.Instance.Name}'.";

        await ApplyProfileAsync(result.Instance, savegame);

        await ReloadAsync(row.Id);
    }

    /// <summary>
    /// The mod half, last and separately. Checking out a save derives and applies its profile where the
    /// adapter has mods, and is simply "write the slot" where it does not - and a user who wanders off
    /// after the claim still holds the save and has it on disk.
    /// </summary>
    private async Task ApplyProfileAsync(LocalInstance instance, SavegameDto savegame)
    {
        if (_repo.Adapter.CanSupportMods is false)
        {
            return;
        }

        if (FindProfile(savegame.ProfileId) is not ProfileDto profile)
        {
            return;
        }

        var plan = await _applyService.TryPlanAsync(_repo, instance, profile.Id, profile.Name, _lifetime);

        if (plan is null)
        {
            Status += $" '{instance.Name}' could not be reached, so its mod folder was left as it is.";

            return;
        }

        // Nothing unrecognised in the folder means there is nothing to disclose, and the ordinary night
        // stays one click.
        if (plan.Unrecognised.Count == 0)
        {
            var outcome = await _applyService.ApplyAsync(
                _repo, instance, profile.Id, profile.Name, confirmPlan: false, progress: null, _lifetime);

            RecordActiveProfile(instance, profile, outcome.Status is not ProfileApplyStatus.Declined);

            Status += $" {outcome.Message}";

            await _driftMonitor.CheckAsync();

            return;
        }

        // Otherwise the drift notice's own two verbs, because this is that problem found at a different
        // moment. Importing is deliberately not on offer here: it would commit files nobody decided to
        // keep, which is the argument that already put import behind Save in the editor.
        var names = plan.Unrecognised.Take(10).Select(x => $"  {x.DisplayName}");
        var more = plan.Unrecognised.Count > 10 ? $"\n  ...and {plan.Unrecognised.Count - 10} more" : "";

        var choice = new ConfirmationDialogViewModel(
            $"The mod folder has {plan.Unrecognised.Count} mods that are not in the repo",
            $"{string.Join('\n', names)}{more}",
            IconKind.Warning,
            $"Apply - puts the folder on '{profile.Name}'. The {plan.Unrecognised.Count} mods go to the Recycle Bin",
            $"Review - opens '{profile.Name}'s mod list with this folder scanned");

        await _modalService.Show(choice);

        if (choice.Result is false)
        {
            // Review leaves the instance drifted, and the persistent notification takes it from there -
            // the same answer this design gives for an instance that cannot be applied to right now.
            RecordActiveProfile(instance, profile, true);

            Status += " The mod folder was left as it is until you decide what to keep.";

            await _driftMonitor.CheckAsync();
            await _shellNavigation.GoToProfileModsAsync(_repo.Id, profile.Id, instance.Id);

            return;
        }

        var result = await _syncService.ExecuteAsync(plan, null, _lifetime);

        RecordActiveProfile(instance, profile, true);

        Status += result.Completed
            ? $" '{instance.Name}' now matches '{profile.Name}'."
            : $" {result.Failures.Count} mods could not be applied to '{instance.Name}'.";

        await _driftMonitor.CheckAsync();
    }

    /// <summary>
    /// The standing intent is recorded even where the folder could not be put right: the instance is
    /// still meant to follow this profile, and being left drifted is what the notice is for.
    /// </summary>
    private void RecordActiveProfile(LocalInstance instance, ProfileDto profile, bool record)
    {
        if (record)
        {
            _localInstanceRepository.SetActiveProfile(instance, new ActiveProfile(_repo.Id, profile.Id));
        }
    }

    /// <summary>
    /// Everything the dialog needs about one instance: its slots and their safety, what the mod folder
    /// would have to do, and how far the save's revision is from the profile's.
    /// </summary>
    private async Task<SavegameCheckOutContext> BuildContextAsync(
        SavegameListItemViewModel row,
        LocalInstance instance,
        SavegameCheckOutMode mode,
        CancellationToken cancellationToken)
    {
        var slots = await _savegameService.GetSlotsAsync(instance, cancellationToken);
        var options = new List<SavegameSlotOptionViewModel>();

        foreach (var slot in slots)
        {
            var availability = await _savegameService.ClassifySlotAsync(instance, slot.Id, cancellationToken);
            var binding = _bindingStore.GetBindingForSlot(instance.Id, slot.Id);

            options.Add(new SavegameSlotOptionViewModel(
                slot,
                availability,
                binding?.SavegameId,
                binding is SavegameCheckoutBinding held
                    ? Savegames.FirstOrDefault(x => x.Id == held.SavegameId)?.Name
                    : null));
        }

        var suggested = await _savegameService.SuggestSlotAsync(instance, row.Id, cancellationToken);
        var hint = _bindingStore.GetSlotHint(instance.Id, row.Id);

        return new SavegameCheckOutContext(
            instance,
            options,
            suggested,
            DescribeSuggestion(options, suggested, hint),
            mode is SavegameCheckOutMode.CheckOut
                ? await BuildModsSummaryAsync(row, instance, cancellationToken)
                : null,
            await BuildRevisionNoteAsync(row));
    }

    /// <summary>
    /// Why the pre-selection is what it is, said plainly - and nothing at all in the ordinary case,
    /// where the slot this save was last in is free and the sentence would only be noise.
    /// </summary>
    private static string? DescribeSuggestion(
        IReadOnlyList<SavegameSlotOptionViewModel> options,
        SavegameSlotId? suggested,
        string? hint)
    {
        if (suggested is null)
        {
            return options.Count == 0
                ? "This instance reports no savegame slots at all."
                : "Every slot has something in it, so there is nothing to pre-select. Pick the one to write over - anything ModsDude has a copy of can be put back.";
        }

        if (hint is null || string.Equals(hint, suggested.Value.Value, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var taken = options.FirstOrDefault(x => string.Equals(x.Id.Value, hint, StringComparison.OrdinalIgnoreCase));

        return taken is null
            ? "The slot this save was last in is gone, so the first free one is picked instead."
            : $"The slot this save was last in now holds '{taken.Label}', so the first free one is picked instead.";
    }

    /// <summary>
    /// What the mod folder would have to do. Null where the adapter has no mods or the folder cannot be
    /// read - the section is absent rather than saying nothing at length.
    /// </summary>
    private async Task<SavegameModsSummary?> BuildModsSummaryAsync(
        SavegameListItemViewModel row,
        LocalInstance instance,
        CancellationToken cancellationToken)
    {
        if (_repo.Adapter.CanSupportMods is false || FindProfile(row.Savegame.ProfileId) is not ProfileDto profile)
        {
            return null;
        }

        var plan = await _applyService.TryPlanAsync(_repo, instance, profile.Id, profile.Name, cancellationToken);

        if (plan is null)
        {
            return null;
        }

        if (plan.HasWork is false)
        {
            return new SavegameModsSummary(true, "Mods are already correct.", [], null);
        }

        var parts = new List<string>();

        if (plan.InstallCount > 0) parts.Add($"{plan.InstallCount} to install");
        if (plan.ReplaceCount > 0) parts.Add($"{plan.ReplaceCount} to replace");
        if (plan.UninstallCount > 0) parts.Add($"{plan.UninstallCount} to uninstall");
        if (plan.RenameCount > 0) parts.Add($"{plan.RenameCount} to rename");

        // A rename leaves the bytes alone, so a locked mod being renamed is not a mod changing
        // under a savegame and is not worth warning about.
        var locked = plan.Items
            .Where(x => x.Locked && x.Action is not (ModSyncAction.Keep or ModSyncAction.Rename))
            .Select(x => $"'{x.DisplayName}'")
            .ToList();

        return new SavegameModsSummary(
            false,
            string.Join(", ", parts) + $" · {plan.KeepCount} already correct.",
            locked,
            plan.Unrecognised.Count > 0
                ? $"{plan.Unrecognised.Count} mods in the folder are not in the repo. You are asked about those separately, before anything moves."
                : null);
    }

    /// <summary>
    /// Which revision the save was last played on against the one the profile is now at. Absent where
    /// they are the same, which is the common case and the one worth saying nothing about.
    /// </summary>
    private async Task<SavegameRevisionNote?> BuildRevisionNoteAsync(SavegameListItemViewModel row)
    {
        if (row.Savegame.Head is not SavegameVersionDto head ||
            FindProfile(row.Savegame.ProfileId) is not ProfileDto profile ||
            profile.HeadRevision <= head.ProfileRevision)
        {
            return null;
        }

        var moved = await LockedPinMovedAsync(profile.Id, head.ProfileRevision, profile.HeadRevision);

        var text = $"Last played on revision {head.ProfileRevision}; {profile.Name} is now at {profile.HeadRevision}.";

        return new SavegameRevisionNote(
            moved
                ? text + " A locked mod moved between them, and hosting this save on it may damage it."
                : text,
            moved);
    }

    private ProfileDto? FindProfile(Guid profileId)
        => _profileService.Profiles.FirstOrDefault(x => x.Id == profileId && x.RepoId == _repo.Id);


    public class Factory(IServiceProvider serviceProvider)
    {
        public RepoSavegamesPageViewModel Create(Repo repo)
            => ActivatorUtilities.CreateInstance<RepoSavegamesPageViewModel>(serviceProvider, repo);
    }
}
