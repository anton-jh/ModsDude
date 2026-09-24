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
    private readonly SavegameSightingCache _sightings;
    private readonly ISavegameService _savegameService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileService _profileService;
    private readonly CurrentUserService _currentUserService;
    private readonly ProfileApplyService _applyService;
    private readonly DriftMonitor _driftMonitor;
    private readonly SavegameFlowService _flowService;
    private readonly ShellNavigationService _shellNavigation;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly IBackgroundProblemReporter _problems;
    private readonly IToastService _toasts;

    private readonly CancellationTokenSource _pageLifetime = new();
    private readonly CancellationToken _lifetime;

    private const string _unseenProfileName = SavegameFlowService.UnseenProfileName;

    private IReadOnlyList<SavegameDto> _fetched = [];
    private string? _currentUserId;


    public RepoSavegamesPageViewModel(
        Repo repo,
        ISavegamesClient savegamesClient,
        ISavegameService savegameService,
        SavegameSightingCache sightings,
        SavegameBindingStore bindingStore,
        ProfileService profileService,
        CurrentUserService currentUserService,
        ProfileApplyService applyService,
        DriftMonitor driftMonitor,
        SavegameFlowService flowService,
        ShellNavigationService shellNavigation,
        IModalService modalService,
        IErrorReporter errorReporter,
        IBackgroundProblemReporter problems,
        IToastService toasts,
        bool showPastSavegames = false)
    {
        _problems = problems;
        _toasts = toasts;
        _showPastSavegames = showPastSavegames;
        _repo = repo;
        _savegamesClient = savegamesClient;
        _savegameService = savegameService;
        _sightings = sightings;
        _bindingStore = bindingStore;
        _profileService = profileService;
        _currentUserService = currentUserService;
        _applyService = applyService;
        _driftMonitor = driftMonitor;
        _flowService = flowService;
        _shellNavigation = shellNavigation;
        _modalService = modalService;
        _errorReporter = errorReporter;

        // Captured once, so that work still in flight after Dispose reads a cancelled token rather
        // than an ObjectDisposedException off the source it came from.
        _lifetime = _pageLifetime.Token;

        IsMember = repo.MembershipLevel >= RepoMembershipLevel.Member;

        // Admin, like pruning a profile's revisions and for the same reason: it destroys a backup,
        // which is not part of running a repo.
        CanPruneSnapshots = repo.MembershipLevel >= RepoMembershipLevel.Admin;

        Savegames = [];
        Timeline = [];
    }


    public string RepoName => _repo.Name;

    /// <summary>
    /// Whether this user may take a claim at all. Reading the list and copying a snapshot is not gated.
    /// </summary>
    /// <remarks>
    /// The coarse half of the answer. Whether a particular savegame can be taken <em>here and now</em>
    /// is the row's <see cref="SavegameListItemViewModel.CanCheckOut"/>, which also knows what this
    /// machine is holding and where its mod folder is.
    /// </remarks>
    public bool IsMember { get; }

    /// <summary>Whether deleting a snapshot of a savegame's history is on offer. Admin only.</summary>
    public bool CanPruneSnapshots { get; }

    public ObservableCollection<SavegameListItemViewModel> Savegames { get; }

    /// <summary>
    /// Whether the savegames their profiles have moved on from are in the list.
    /// </summary>
    /// <remarks>
    /// <b>Off by default, and a toggle rather than a second list.</b> Past savegames stay findable without
    /// filling a list somebody opened to find the one they are playing tonight - and keeping them here
    /// is what stops this becoming a list per profile, which the whole savegame-is-an-attribute
    /// argument exists to avoid. See docs/10-savegame-profile-binding.md#savegames-list.
    /// </remarks>
    [ObservableProperty]
    private bool _showPastSavegames;

    /// <summary>How many rows the toggle is currently keeping out of the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenPast))]
    [NotifyPropertyChangedFor(nameof(HiddenPastText))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private int _hiddenPastCount;

    public bool HasHiddenPast => HiddenPastCount > 0;

    /// <summary>
    /// What every save in the repo adds up to - how many, how many snapshots, and how many bytes of history.
    /// Over all of them, hidden past ones included: what a repo carries does not depend on what the list
    /// is showing. Empty for a repo with none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatistics))]
    private string _statisticsText = "";

    public bool HasStatistics => StatisticsText.Length > 0;

    public string HiddenPastText => HiddenPastCount == 1
        ? "1 past savegame is hidden."
        : $"{HiddenPastCount} past savegames are hidden.";

    /// <summary>
    /// What an empty list says, which is not the same sentence when the toggle is what emptied it.
    /// </summary>
    /// <remarks>
    /// A repo whose every savegame is past reads as a repo with no savegames at all otherwise, and "publish
    /// one" is advice for a state this is not in.
    /// </remarks>
    public string EmptyText => HasHiddenPast
        ? "Every save here is a past savegame. Turn on 'Show past savegames' to see them - they are still playable."
        : "No saves here yet. Publish one of the saves already on this machine, and it appears in this list for everybody.";

    /// <summary>Snapshots and checkouts as one column, newest first.</summary>
    public ObservableCollection<SavegameTimelineEntryViewModel> Timeline { get; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveSavegameCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameSavegameCommand))]
    private SavegameListItemViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    [NotifyCanExecuteChangedFor(nameof(CheckOutSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeCopySnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSnapshotCommand))]
    private SavegameTimelineEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isLoadingTimeline;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckOutSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeCopySnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameSavegameCommand))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveSavegameCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishSaveCommand))]
    private bool _isWorking;

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

        await ForgetDeletedHoldsAsync();
    }

    /// <summary>
    /// Drops the holds this machine keeps for savegames the repo no longer has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Done rather than asked.</b> A binding whose savegame has been archived and then deleted for
    /// good names something nobody can produce: there is no claim left to hand back, no history to
    /// check a snapshot into, and every server-side verb on it answers 404. The only thing anybody can
    /// do about it is stop tracking it, and a dialog offering a choice with one sane answer is a
    /// dialog that exists to be clicked through. So the row is not built, the button is not offered,
    /// and the state is simply gone the next time this list is opened.
    /// </para>
    /// <para>
    /// <b>Nothing on disk is touched</b>, which is what makes this safe to do unasked. The save stays
    /// exactly where it is and becomes an ordinary save of the user's own - the same outcome the
    /// button had, reached without the question.
    /// </para>
    /// <para>
    /// <b>Only on two good reads, and only for this repo's bindings.</b> A failed round trip must
    /// never be read as a deletion - that is how an app comes to forget somebody's savegame over a
    /// flaky connection - so anything less than both lists arriving leaves every binding alone. The
    /// archived list is read as well as the live one because archiving deliberately does not release
    /// a hold: an archived savegame is still perfectly real and still checks in.
    /// </para>
    /// </remarks>
    private async Task ForgetDeletedHoldsAsync()
    {
        if (_repo.Games.FirstOrDefault() is not Game game)
        {
            return;
        }

        var held = _bindingStore.GetBindings(game.Identity)
            .Where(x => x.RepoId == _repo.Id)
            .ToList();

        if (held.Count == 0)
        {
            return;
        }

        HashSet<Guid> known;

        try
        {
            known =
            [
                .. _fetched.Select(x => x.Id),
                .. (await _savegamesClient.GetArchivedSavegamesV1Async(_repo.Id, _lifetime)).Select(x => x.Id)
            ];
        }
        catch (Exception)
        {
            // Nothing was found out, so nothing is forgotten - see the remarks. Every exception and
            // not just ApiException: this is optional housekeeping on the way into a page, and a
            // transport failure taking the savegame list down with it would be a far worse trade
            // than a binding that gets swept on the next visit instead.
            return;
        }

        foreach (var binding in held.Where(x => known.Contains(x.SavegameId) is false))
        {
            _savegameService.Forget(game, binding.SavegameId);
        }
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
    /// Makes a savegame out of a save that is already on this disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here rather than on the game's own slot list</b>, which is where it used to be a button on
    /// a row. Publishing is the way a repo's first savegame comes into existence, so it has to be
    /// reachable from the list that is empty and saying so - and under one game per machine there is
    /// no sidebar of installations to go looking through for it.
    /// </para>
    /// <para>
    /// <b>The flow is <see cref="SavegameFlowService.PublishAsync"/>'s</b>, because a profile's Overview
    /// offers the same publish, opened on that profile.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishSave()
    {
        IsWorking = true;

        try
        {
            await _flowService.PublishAsync(_repo, preselectProfileId: null, id => ReloadAsync(id), _lifetime);
        }
        finally
        {
            IsWorking = false;
        }
    }

    // Member, like checking in and archiving: it writes to the repo, and everybody in it sees the
    // result.
    private bool CanPublish() => IsMember && IsWorking is false;

    /// <summary>
    /// Checks out the selected entry's snapshot. Where that is not the head it is a restore first -
    /// copied forward as a new snapshot, with nothing in between deleted - which is why there is no
    /// separate restore flow to find.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanActOnEntry))]
    private async Task CheckOutSnapshot()
    {
        if (Selected is SavegameListItemViewModel row && SelectedEntry?.SnapshotNumber is int number)
        {
            await StartAsync(row, number, SavegameCheckOutMode.CheckOut);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyEntry))]
    private async Task TakeCopySnapshot()
    {
        if (Selected is SavegameListItemViewModel row && SelectedEntry?.SnapshotNumber is int number)
        {
            await StartAsync(row, number, SavegameCheckOutMode.TakeCopy);
        }
    }

    /// <summary>
    /// Renames the selected savegame. That is the whole of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing else moves with the name.</b> The snapshots, the claim log, whoever is holding it and
    /// which mod list it follows are all untouched - a savegame cannot be moved between profiles, so
    /// there is no second field this dialog could grow. The server's route says the same thing from
    /// its end: it became a rename in Phase 9 and takes nothing but a name.
    /// </para>
    /// <para>
    /// <b>The clash is the server's to find.</b> Names are unique per repo behind a filtered unique
    /// index, so checking here first would be a second copy of a rule that would still be racing
    /// somebody else's rename. Losing that race re-opens the dialog with what they typed rather than
    /// an error they have to start over from.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRenameSelected))]
    private async Task RenameSavegame()
    {
        if (Selected is not SavegameListItemViewModel row)
        {
            return;
        }

        var title = $"Rename '{row.Name}'";
        var message = "What everybody else in this repo will see it called. Its snapshots, its history and "
            + "whoever is holding it are untouched, and so is the mod list it follows.";
        var suggested = row.Name;

        try
        {
            // Until they give a free name or give up. A taken one is not an error to report and walk
            // away from - the person is standing right here and is the one who knows what else it
            // could be called.
            while (await AskForNameAsync(title, message, suggested) is string name)
            {
                if (name == row.Name)
                {
                    return;
                }

                IsWorking = true;

                try
                {
                    await _savegamesClient.UpdateSavegameV1Async(
                        _repo.Id, row.Id, new UpdateSavegameRequest { Name = name }, _lifetime);
                }
                catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.NameTaken)
                {
                    title = "That name is taken";
                    message = $"Something else in this repo is already called '{name}'. Pick another.";
                    suggested = name;

                    continue;
                }
                finally
                {
                    IsWorking = false;
                }

                _toasts.Show($"'{row.Name}' is called '{name}' now.");

                await ReloadAsync(row.Id);

                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "renaming a savegame");
        }
    }

    // Member, like archiving: it changes what everybody in the repo sees this save called, and it is
    // reversible by doing it again.
    private bool CanRenameSelected() => IsMember && IsWorking is false && Selected is not null;

    private async Task<string?> AskForNameAsync(string title, string message, string suggested)
    {
        var modal = new RenameModalViewModel(title, message, suggested, "Rename it");

        await _modalService.Show(modal);

        return modal.Result;
    }

    /// <summary>
    /// Puts the selected savegame in the repo's Archive.
    /// </summary>
    /// <remarks>
    /// The only way a savegame goes away, and deliberately not a delete: what it carries is backups
    /// of somebody's play. It keeps its snapshots and its claim log - archiving a save somebody is
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
    // repo's saves tidy. CanPruneSnapshots is the Admin one, and gates deleting a snapshot.
    private bool CanArchiveSelected() => IsMember && IsWorking is false && Selected is not null;

    /// <summary>
    /// Deletes the selected snapshot from the savegame's history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Admin only, and never the head.</b> It destroys a backup, which is not part of running a
    /// repo; and the head is what a check-out hands people, so a savegame whose current snapshot is
    /// missing is one nobody can play. The server refuses both, and the button is simply absent
    /// rather than present-and-doomed.
    /// </para>
    /// <para>
    /// <b>The reason this exists is a profile's history.</b> A snapshot pins the profile revision it
    /// was played on, so it is what stops that revision being pruned - and "played on save X snapshot
    /// 3" would be an obstacle somebody could see and never move.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDeleteEntry))]
    private async Task DeleteSnapshot()
    {
        if (Selected is not SavegameListItemViewModel row || SelectedEntry?.SnapshotNumber is not int number)
        {
            return;
        }

        var confirmation = new ConfirmationDialogViewModel(
            $"Delete snapshot {number}?",
            $"This copy of '{row.Name}' goes for good. The others stay, and whoever is playing it now "
                + "is unaffected - they hold the current snapshot, which this is not.",
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
            await _savegamesClient.DeleteSavegameSnapshotV1Async(_repo.Id, row.Id, number, _pageLifetime.Token);

            await LoadTimelineAsync(row);
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "deleting a savegame snapshot");
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanDeleteEntry()
        => CanPruneSnapshots && IsWorking is false && SelectedEntry is { IsSnapshot: true, IsHead: false };

    private bool CanActOnEntry() => IsMember && IsWorking is false && SelectedEntry is { IsSnapshot: true };
    private bool CanCopyEntry() => IsWorking is false && SelectedEntry is { IsSnapshot: true };

    /// <summary>
    /// Into the profile's own history, where two revisions can be compared properly. The comparison
    /// already exists; repeating a cut-down snapshot of it here would be a second answer to maintain.
    /// </summary>
    [RelayCommand]
    private async Task CompareRevisions()
    {
        // A savegame that follows no mod list has no revisions to compare, the same way an
        // unselected row has nothing to open.
        if (Selected is not SavegameListItemViewModel row || row.Savegame.ProfileId is not Guid profileId)
        {
            return;
        }

        if (await _shellNavigation.GoToProfileHistoryAsync(_repo.Id, profileId) is false)
        {
            _toasts.Show($"'{row.ProfileName}' could not be opened from here.", ToastSeverity.Warning);
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
        // "somebody took this over" is fed from this one place rather than from each fetch. The claim
        // watch feeds it too, for the repos this machine holds a save in, whether or not this page is
        // open.
        _sightings.Record(_repo.Id, savegames, _currentUserId);

        var wanted = select ?? Selected?.Id;

        ClearRows();

        // Past is a fact about which savegame a profile is following, so the toggle hides rows rather than
        // marking them differently - and the count is said out loud, because a filter nobody can see
        // is a list that is quietly wrong.
        var shown = savegames.Where(x => ShowPastSavegames || x.SupersededAt is null).ToList();

        HiddenPastCount = savegames.Count - shown.Count;

        StatisticsText = DescribeStatistics(SavegameStatistics.From(savegames));

        // Two people called Anton can both hold a save in this repo, and neither of them is the
        // duplicate - so the tag goes on both or on neither, decided over this list.
        var ambiguous = UserDisplay.FindAmbiguous(
            savegames.Select(x => x.Checkout?.User).OfType<UserDto>());

        // One read of the game's folder state for the whole list, rather than one per row: a
        // manifest is every mod in the profile with a hash each, and twenty rows must not cost twenty
        // parses of it.
        var host = _flowService.ReadHost(_repo);

        foreach (var savegame in InListOrder(shown))
        {
            var row = new SavegameListItemViewModel(
                savegame,
                FindProfile(savegame.ProfileId)?.Name ?? _unseenProfileName,
                _currentUserId,
                IsMember,
                ambiguous.Contains(savegame.Checkout?.User.Id ?? ""));

            row.CheckOutRequested += OnCheckOutRequested;
            row.CheckInRequested += OnCheckInRequested;
            row.DiscardRequested += OnDiscardRequested;
            row.DisconnectRequested += OnDisconnectRequested;
            row.TakeCopyRequested += OnTakeCopyRequested;
            row.ApplyProfileRequested += OnApplyProfileRequested;
            row.MakeCurrentRequested += OnMakeCurrentRequested;

            Savegames.Add(row);
        }

        // After the rows exist, because a refusal names the savegame in the way - which is a row in
        // this same list.
        foreach (var row in Savegames)
        {
            _flowService.Offer(_repo, row, host, NameOfHeld);
        }

        IsEmpty = Savegames.Count == 0;

        // Assigning this is what loads the timeline, so it happens after the rows exist.
        Selected = Savegames.FirstOrDefault(x => x.Id == wanted) ?? Savegames.FirstOrDefault();

        _ = AnnotateAsync([.. Savegames]);
    }

    /// <summary>
    /// A profile's savegames together, its current one first and the past ones under it.
    /// </summary>
    /// <remarks>
    /// <b>Grouped by profile rather than sorted by name</b>, because a past savegame is only meaningful
    /// beside the one that displaced it. Past ones run most recently displaced first - the one you
    /// were playing before this is the one you are likeliest to be looking for - and savegames that
    /// follow no mod list have no group to sit in, so they come last.
    /// </remarks>
    private IEnumerable<SavegameDto> InListOrder(IEnumerable<SavegameDto> savegames)
        => savegames
            .OrderBy(x => x.ProfileId is null)
            .ThenBy(x => FindProfile(x.ProfileId)?.Name ?? _unseenProfileName, NaturalOrder.Comparer)
            .ThenBy(x => x.ProfileId)
            .ThenBy(x => x.SupersededAt is not null)
            .ThenByDescending(x => x.SupersededAt)
            .ThenBy(x => x.Name, NaturalOrder.Comparer);

    private static string DescribeStatistics(SavegameStatistics statistics)
    {
        if (statistics.Savegames == 0)
        {
            return "";
        }

        var saves = statistics.Savegames == 1 ? "1 savegame" : $"{statistics.Savegames:N0} savegames";
        var snapshots = statistics.Snapshots == 1 ? "1 snapshot" : $"{statistics.Snapshots:N0} snapshots";

        return $"{saves} · {snapshots} · {ByteSize.Describe(statistics.TotalBytes)} stored";
    }

    private void ClearRows()
    {
        foreach (var row in Savegames)
        {
            row.CheckOutRequested -= OnCheckOutRequested;
            row.CheckInRequested -= OnCheckInRequested;
            row.DiscardRequested -= OnDiscardRequested;
            row.DisconnectRequested -= OnDisconnectRequested;
            row.TakeCopyRequested -= OnTakeCopyRequested;
            row.ApplyProfileRequested -= OnApplyProfileRequested;
            row.MakeCurrentRequested -= OnMakeCurrentRequested;
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

    partial void OnShowPastSavegamesChanged(bool value)
    {
        // From what was fetched rather than from the server: the toggle changes which rows are drawn,
        // not what the repo holds.
        if (IsLoading is false)
        {
            Publish(_fetched);
        }
    }

    /// <summary>
    /// What a savegame in the way is called. Read off this list, which is where the refusal has to
    /// point anyway - and null for one the toggle is hiding or the repo will not show, where the
    /// refusal stands without the name.
    /// </summary>
    private string? NameOfHeld(Guid savegameId)
        => savegameId == Guid.Empty
            ? null
            : Savegames.FirstOrDefault(x => x.Id == savegameId)?.Name
                ?? _fetched.FirstOrDefault(x => x.Id == savegameId)?.Name;

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
            // Logged and left to the notice column rather than raised as a dialog: the list is
            // correct without these chips, and a modal over a list that loaded fine is the wrong size
            // of answer for a missing caption.
            _errorReporter.Record(exception, "checking the savegame list against this machine");
            _problems.Report(BackgroundProblem.DeferredLoad);
        }
    }

    private async Task AnnotateRevisionsAsync(IReadOnlyList<SavegameListItemViewModel> rows)
    {
        foreach (var row in rows)
        {
            _lifetime.ThrowIfCancellationRequested();

            if (row.Savegame.Head is not SavegameSnapshotDto head ||
                head.ProfileRevision is not int played ||
                FindProfile(row.Savegame.ProfileId) is not ProfileDto profile)
            {
                continue;
            }

            var behind = profile.HeadRevision - played;

            if (behind <= 0)
            {
                continue;
            }

            row.SetRevisionDrift(
                behind,
                await _flowService.LockedPinMovedAsync(_repo.Id, profile.Id, played, profile.HeadRevision, _lifetime));
        }
    }

    private async Task AnnotateUnpublishedPlayAsync(IReadOnlyList<SavegameListItemViewModel> rows)
    {
        foreach (var game in _repo.Games.ToList())
        {
            // A hold whose folder the settings no longer name has nothing to hash, so there is
            // nothing this chip could say about it. The row says that state in its own words instead,
            // and offers the one action it has - see SavegameListItemViewModel.HoldNote.
            var unreachable = _savegameService.GetUnreachableHolds(game)
                .Select(x => x.SavegameId)
                .ToHashSet();

            foreach (var binding in _bindingStore.GetBindings(game.Identity))
            {
                _lifetime.ThrowIfCancellationRequested();

                if (rows.FirstOrDefault(x => x.Id == binding.SavegameId) is not SavegameListItemViewModel row
                    || unreachable.Contains(binding.SavegameId))
                {
                    continue;
                }

                var availability = await _savegameService.ClassifySlotAsync(game, binding.Slot, _lifetime);

                if (availability is SavegameSlotAvailability.HeldWithUnpublishedPlay)
                {
                    row.SetUnpublishedPlay(true);
                }
            }
        }
    }

    /// <summary>
    /// Snapshots and checkouts, merged and ordered newest first. Two reads rather than one, because the
    /// server keeps them as two logs on purpose - the checkout rows outlive the blobs, so history can
    /// still say that a snapshot existed and was pruned.
    /// <para>
    /// A claim becomes up to two rows, at the two moments it actually happened: see the remarks on
    /// <see cref="SavegameTimelineEntryViewModel"/>.
    /// </para>
    /// </summary>
    private async Task LoadTimelineAsync(SavegameListItemViewModel row)
    {
        IsLoadingTimeline = true;

        try
        {
            var snapshots = await _savegamesClient.GetSavegameSnapshotsV1Async(
                _repo.Id, row.Id, null, null, _lifetime);

            var checkouts = await _savegamesClient.GetSavegameCheckoutsV1Async(
                _repo.Id, row.Id, null, null, _lifetime);

            // The selection can have moved on while this was in flight, in which case this answer is
            // about a savegame nobody is looking at any more.
            if (ReferenceEquals(Selected, row) is false)
            {
                return;
            }

            // Which claims a snapshot already speaks for. Those get no ending row of their own - the
            // snapshot minted against a claim is the check-in, and a thin "Checked back in" at the same
            // second would only say it again. Taken from the loaded window rather than from the whole
            // history on purpose: a check-in whose snapshot has since been pruned, or scrolled past,
            // then gets its ending drawn, which is the point of the claim log outliving the blobs.
            var recorded = snapshots.Snapshots
                .Where(x => x.CheckoutId is not null)
                .Select(x => x.CheckoutId!.Value)
                .ToHashSet();

            // Newest first, and the rank behind it carries weight rather than tidying: publishing,
            // checking in and taking a save over each write two rows off one clock reading, so the
            // moment alone leaves the tie to whichever read was concatenated first - which is what put
            // a publish above the claim it opened and made the save look checked out before it existed.
            var entries = snapshots.Snapshots
                .Select(x => SavegameTimelineEntryViewModel.ForSnapshot(x, x.Number == snapshots.HeadSnapshot))
                .Concat(checkouts.Checkouts.Select(SavegameTimelineEntryViewModel.ForClaimTaken))
                .Concat(checkouts.Checkouts
                    .Where(x => x.EndedAt is not null && recorded.Contains(x.Id) is false)
                    .Select(SavegameTimelineEntryViewModel.ForClaimEnded))
                .OrderByDescending(x => x.Moment)
                .ThenByDescending(x => x.Rank)
                .ThenByDescending(x => x.SnapshotNumber);

            Timeline.Clear();

            foreach (var entry in entries)
            {
                Timeline.Add(entry);
            }

            HasOlder = snapshots.HasMore || checkouts.HasMore;

            SelectedEntry = Timeline.FirstOrDefault();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Timeline.Clear();
            IsLoadingTimeline = false;

            await _errorReporter.ShowAsync(exception, $"reading the history of '{row.Name}'");
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

    /// <summary>
    /// Hands a save back from the game holding it, without going to that game's own page.
    /// </summary>
    /// <remarks>
    /// <b>The game is the row's, not a choice.</b> A check-in uploads what is in a slot, so the
    /// only game it can mean is the one whose slot holds the copy - which is why this reads
    /// <see cref="SavegameListItemViewModel.HeldHere"/> and not <c>Host</c>, and why there is no
    /// picker here the way there is for a check-out.
    /// </remarks>
    private async void OnCheckInRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameListItemViewModel row || row.HeldHere is not Game game)
        {
            return;
        }

        IsWorking = true;

        try
        {
            await _flowService.CheckInHeldAsync(game, row.Id, row.Name, () => ReloadAsync(row.Id), _lifetime);
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// Gives a save back without minting a snapshot - taken by mistake, never played.
    /// </summary>
    /// <remarks>
    /// <b>Beside Check in, because it is the other answer to the same question.</b> It was a row
    /// action on the game's own slot list, which is one page and one sidebar away from the list
    /// somebody is looking at when they realise they took the wrong save - and the flow it runs is
    /// the same one, dialog and all.
    /// </remarks>
    private async void OnDiscardRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameListItemViewModel row || row.Hold is not SavegameHoldHere hold)
        {
            return;
        }

        IsWorking = true;

        try
        {
            // Asked of the disk here rather than read off the row's chip. The chip arrives from a
            // background pass that may not have reached this row yet, and the two confirmations this
            // decides between are "nothing is lost" and "an evening of play goes to the Recycle Bin".
            // A stale false there is the one wrong answer this whole feature cannot afford, and it
            // costs one slot hash at the moment somebody is about to be asked anyway.
            var played = await _savegameService.ClassifySlotAsync(hold.Game, hold.Slot, _lifetime)
                is SavegameSlotAvailability.HeldWithUnpublishedPlay;

            // The savegame's name where the dialog wants a slot label, as the check-in does: a slot
            // id is a folder name the player has never thought in, and what they are giving back is
            // the save rather than the folder.
            var discarded = await _flowService.DiscardAsync(
                hold.Game, row.Id, row.Name, row.Name, played, _lifetime);

            if (discarded is false)
            {
                return;
            }

            _toasts.Show($"'{row.Name}' was given back without a snapshot. The local copy is in the Recycle Bin.");

            await _driftMonitor.CheckAsync();
            await ReloadAsync(row.Id);
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-discard. There is no page left to report on.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "giving a savegame back");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// Stops tracking a copy sitting in a folder the game's settings no longer name.
    /// </summary>
    /// <remarks>
    /// The only state this is offered in, and the only thing offered in it: check in and discard both
    /// need the bytes, and there is no folder left to read them from. A hold whose <em>savegame</em>
    /// the repo has deleted never gets this far - it is dropped on sight, see
    /// <see cref="ForgetDeletedHoldsAsync"/>.
    /// </remarks>
    private async void OnDisconnectRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameListItemViewModel row || row.Hold is not SavegameHoldHere hold)
        {
            return;
        }

        IsWorking = true;

        try
        {
            if (await _flowService.DisconnectAsync(hold.Game, row.Id, row.Name, hold.FolderName) is false)
            {
                return;
            }

            _toasts.Show($"ModsDude has stopped tracking '{row.Name}'. The save is still on this disk, and the claim is still yours.");

            await _driftMonitor.CheckAsync();
            await ReloadAsync(row.Id);
        }
        catch (OperationCanceledException)
        {
            // Navigated away. Forgetting a binding is local and already done or not done.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "disconnecting a savegame");
        }
        finally
        {
            IsWorking = false;
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
    /// Puts a past savegame back in its profile's current slot.
    /// </summary>
    /// <remarks>
    /// <b>Stated before it runs</b>, like the publish that performs the same swap from the other end:
    /// this is one of the two things that change which savegame a profile is following, and the one it
    /// displaces is somebody's. The incumbent is named from this list where it is in it - an archived
    /// one is not, and still holds the slot - and the server's answer names it exactly afterwards.
    /// </remarks>
    private async void OnMakeCurrentRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameListItemViewModel row)
        {
            return;
        }

        var incumbent = _fetched.FirstOrDefault(x =>
            x.ProfileId == row.Savegame.ProfileId && x.SupersededAt is null);

        var confirmation = new ConfirmationDialogViewModel(
            $"Make '{row.Name}' {row.ProfileName}'s current savegame?",
            DescribeSwap(row, incumbent),
            IconKind.Question,
            "Make it current",
            "Leave it as it is");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        IsWorking = true;

        try
        {
            var result = await _savegameService.MakeCurrentAsync(
                [.. _repo.Games], row.Savegame, _lifetime);

            _toasts.Show(result.Superseded is SavegameDto displaced
                ? $"'{row.Name}' is {row.ProfileName}'s current savegame and follows it from here. '{displaced.Name}' is past - still playable, and its mod list no longer moves."
                : $"'{row.Name}' is {row.ProfileName}'s current savegame and follows it from here.");

            await _driftMonitor.CheckAsync();
            await ReloadAsync(row.Id);
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "making a savegame current");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// What the swap costs, in the one paragraph that says it.
    /// </summary>
    /// <remarks>
    /// Past is not archived and not read-only, and the sentence has to carry that or it reads like a
    /// deletion: the displaced savegame stays playable, stays checkable-out, and the one thing that
    /// changes is that its revision stops moving.
    /// </remarks>
    private string DescribeSwap(SavegameListItemViewModel row, SavegameDto? incumbent)
    {
        var moves = row.PinnedRevision is int pinned
            ? $"'{row.Name}' stops being pinned to rev {pinned} and follows {row.ProfileName} again."
            : $"'{row.Name}' follows {row.ProfileName} again.";

        if (incumbent is null)
        {
            // Either the profile has no current savegame - its last one was deleted - or it has one this
            // list is not showing, which means archived. Both are honest without a name.
            return $"{moves} Whichever savegame {row.ProfileName} is following becomes past: it stays playable, and its mod list stops moving.";
        }

        var stays = incumbent.Head?.ProfileRevision is int revision
            ? $"it stays playable and stays on rev {revision}"
            : "it stays playable, and its mod list stops moving";

        return $"'{incumbent.Name}' is {row.ProfileName}'s current savegame. This swaps them: {moves} '{incumbent.Name}' becomes past - {stays}.";
    }

    /// <summary>
    /// The row's other action: puts the mod folder on the list this savegame runs on, which is what
    /// enables the one beside it.
    /// </summary>
    /// <remarks>
    /// <b>It names the revision.</b> Nothing is holding this savegame yet, so an apply that let the
    /// game decide would install head - correct for a current savegame and wrong for a past one,
    /// whose check-out a moment later would leave the folder drifted against the revision it just
    /// pinned.
    /// </remarks>
    private async void OnApplyProfileRequested(object? sender, EventArgs e)
    {
        if (sender is not SavegameListItemViewModel row
            || _repo.Games.FirstOrDefault() is not Game game
            || FindProfile(row.Savegame.ProfileId) is not ProfileDto profile)
        {
            return;
        }

        IsWorking = true;

        try
        {
            // An activation: this savegame follows a mod list and the game is being put on it, so
            // the intent is what the service records before it touches a file.
            var outcome = await _applyService.ActivateAsync(
                _repo,
                game,
                profile.Id,
                profile.Name,
                confirmPlan: false,
                progress: null,
                _lifetime,
                revision: row.PinnedRevision ?? profile.HeadRevision);

            _toasts.Show(outcome.Message, outcome.ToastSeverity);

            await _driftMonitor.CheckAsync();

            // The folder moved, so every row's answer to "can this be checked out here" has moved
            // with it - not just this one's.
            await ReloadAsync(row.Id);
        }
        catch (OperationCanceledException)
        {
            // Navigated away.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, "applying a profile");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// The check-out dialog and everything after it, which is <see cref="SavegameFlowService.CheckOutAsync"/>'s
    /// - a profile's Overview offers the same check-out for its own savegame.
    /// </summary>
    private async Task StartAsync(SavegameListItemViewModel row, int snapshotNumber, SavegameCheckOutMode mode)
    {
        IsWorking = true;

        try
        {
            await _flowService.CheckOutAsync(
                _repo, row.Savegame, snapshotNumber, mode, _currentUserId, NameOfHeld, () => ReloadAsync(row.Id), _lifetime);
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// The profile a savegame follows, or <c>null</c> where it follows none. The same answer as a
    /// profile this member cannot see, and deliberately so: every caller wants the same thing from
    /// both, which is to say nothing about mod lists on that row.
    /// </summary>
    private ProfileDto? FindProfile(Guid? profileId)
        => profileId is Guid id
            ? _profileService.Profiles.FirstOrDefault(x => x.Id == id && x.RepoId == _repo.Id)
            : null;


    public class Factory(IServiceProvider serviceProvider)
    {
        /// <param name="showPastSavegames">
        /// Whether to arrive with the toggle already on. Set by a link from a profile, whose count of
        /// past savegames is only worth clicking if it lands on a list that shows them.
        /// </param>
        public RepoSavegamesPageViewModel Create(Repo repo, bool showPastSavegames = false)
            => ActivatorUtilities.CreateInstance<RepoSavegamesPageViewModel>(serviceProvider, repo, showPastSavegames);
    }
}
