using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.Account;
using ModsDude.Client.Wpf.Shared;
using ModsDude.Client.Wpf.Shell.Modals;
using ModsDude.Client.Wpf.Shell.Navigation;
using ModsDude.Client.Wpf.Shell.Notices;
using ModsDude.Client.Wpf.Shell.Sidebar;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Savegames;

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
public partial class RepoSavegamesPageViewModel : PageViewModel, ISavegameRowActions, IDisposable
{
    private readonly Repo _repo;
    private readonly ISavegamesClient _savegamesClient;
    private readonly ISavegameStore _store;
    private readonly IGameRepository _games;
    private readonly ISavegameSlots _slots;
    private readonly ISavegameHolds _holds;
    private readonly ISavegameBindingStore _bindingStore;
    private readonly IProfileStore _profileStore;
    private readonly ICurrentUserStore _currentUser;
    private readonly IDriftMonitor _driftMonitor;
    private readonly ISavegameOffers _offers;
    private readonly IHeldSavegameNames _heldSavegameNames;
    private readonly ISavegameCompatibilityCheck _compatibilityCheck;
    private readonly ISavegameCheckInFlow _checkInFlow;
    private readonly ISavegameCheckOutFlow _checkOutFlow;
    private readonly ISavegamePublishFlow _publishFlow;
    private readonly ISavegameDisconnectFlow _disconnectFlow;
    private readonly IShellNavigationService _shellNavigation;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly LatestLoad _timelineLoad;
    private readonly LatestLoad _publishLoad;
    private readonly IBackgroundProblemReporter _problems;
    private readonly IToastService _toasts;
    private readonly IUserAvatarFactory _avatarFactory;
    private readonly TimeProvider _time;

    private readonly CancellationTokenSource _pageLifetime = new();
    private readonly CancellationToken _lifetime;

    private IReadOnlyDictionary<Guid, HeldSavegameName> _heldNames = new Dictionary<Guid, HeldSavegameName>();
    private string? _currentUserId;
    private Guid? _selectOnArrival;
    private CancellationTokenSource? _annotation;
    private bool _annotationDue;


    public RepoSavegamesPageViewModel(
        Repo repo,
        ISavegamesClient savegamesClient,
        ISavegameSlots slots,
        ISavegameHolds holds,
        ISavegameStore store,
        IGameRepository games,
        ISavegameBindingStore bindingStore,
        IProfileStore profileStore,
        ICurrentUserStore currentUser,
        IDriftMonitor driftMonitor,
        ISavegameOffers offers,
        IHeldSavegameNames heldSavegameNames,
        ISavegameCompatibilityCheck compatibilityCheck,
        ISavegameCheckInFlow checkInFlow,
        ISavegameCheckOutFlow checkOutFlow,
        ISavegamePublishFlow publishFlow,
        ISavegameDisconnectFlow disconnectFlow,
        IShellNavigationService shellNavigation,
        IModalService modalService,
        IErrorReporter errorReporter,
        IBackgroundProblemReporter problems,
        IToastService toasts,
        IUserAvatarFactory avatarFactory,
        TimeProvider time)
    {
        _time = time;
        _problems = problems;
        _toasts = toasts;
        _avatarFactory = avatarFactory;
        _repo = repo;
        _savegamesClient = savegamesClient;
        _slots = slots;
        _holds = holds;
        _store = store;
        _games = games;
        _bindingStore = bindingStore;
        _profileStore = profileStore;
        _currentUser = currentUser;
        _driftMonitor = driftMonitor;
        _offers = offers;
        _heldSavegameNames = heldSavegameNames;
        _compatibilityCheck = compatibilityCheck;
        _checkInFlow = checkInFlow;
        _checkOutFlow = checkOutFlow;
        _publishFlow = publishFlow;
        _disconnectFlow = disconnectFlow;
        _shellNavigation = shellNavigation;
        _modalService = modalService;
        _errorReporter = errorReporter;

        // Captured once, so that work still in flight after Dispose reads a cancelled token rather
        // than an ObjectDisposedException off the source it came from.
        _lifetime = _pageLifetime.Token;
        _timelineLoad = new LatestLoad(loading => IsLoadingTimeline = loading, _lifetime);
        _publishLoad = new LatestLoad(_ => { }, _lifetime);

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
    /// What every save in the repo adds up to - how many, how many snapshots, and how many bytes of history.
    /// Empty for a repo with none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatistics))]
    private string _statisticsText = "";

    public bool HasStatistics => StatisticsText.Length > 0;

    public string EmptyText => "No saves here yet. Publish one of the saves already on this machine, and it appears in this list for everybody.";

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
        await _profileStore.EnsureLoadedAsync(_repo.Id, _lifetime);

        // Which chip says "You have it" rather than naming somebody. Absorbed: a list that cannot tell
        // whose is whose is still a list, and everything else on the page works.
        try
        {
            _currentUserId = (await _currentUser.GetAsync(_lifetime)).Id;
        }
        catch (ApiException)
        {
            _currentUserId = null;
        }

        await _store.EnsureLoadedAsync(_repo.Id, _lifetime);

        await ForgetDeletedHoldsAsync();

        _heldNames = await ReadHeldNamesAsync(_lifetime);
    }

    /// <summary>
    /// Drops the holds this machine keeps for savegames the repo no longer has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Done rather than asked.</b> A binding whose savegame has been archived and then deleted for
    /// good names something nobody can produce: there is no claim left to hand back, no history to
    /// check a snapshot into, and every server-side verb on it answers 404. The only thing anybody can
    /// do about it is stop tracking it, and a modal offering a choice with one sane answer is a
    /// modal that exists to be clicked through. So the row is not built, the button is not offered,
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

        try
        {
            await _store.EnsureArchivedLoadedAsync(_repo.Id, _lifetime);
        }
        catch (Exception)
        {
            // Nothing was found out, so nothing is forgotten - see the remarks. Every exception and
            // not just ApiException: this is optional housekeeping on the way into a page, and a
            // transport failure taking the savegame list down with it would be a far worse trade
            // than a binding that gets swept on the next visit instead.
            return;
        }

        HashSet<Guid> known = [.. _store.Live(_repo.Id).Select(x => x.Id), .. _store.Archived(_repo.Id).Select(x => x.Id)];

        foreach (var binding in held.Where(x => known.Contains(x.SavegameId) is false))
        {
            _holds.Forget(game, binding.SavegameId);
        }
    }

    protected override void OnInitCompleted()
    {
        var select = _selectOnArrival;
        _selectOnArrival = null;

        Publish(select, annotate: true);

        IsLoading = false;

        // After the first publish, so nothing redraws a list that has not been drawn.
        _store.Changed += OnSavegamesChanged;
        _profileStore.Changed += OnProfilesChanged;
        _bindingStore.BindingsChanged += OnBindingsChanged;
        _games.GameChanged += OnFolderStateChanged;
        _driftMonitor.Changed += OnFolderStateChanged;
    }

    /// <summary>
    /// Which savegame to arrive with selected, for a page opened by a link about one of them.
    /// </summary>
    public void SelectOnArrival(Guid? savegameId) => _selectOnArrival = savegameId;

    /// <summary>
    /// Selects one savegame on a page already open, for a link arriving while the user is on it.
    /// </summary>
    public void Select(Guid savegameId)
    {
        if (IsLoading is false && Savegames.FirstOrDefault(x => x.Id == savegameId) is SavegameListItemViewModel row)
        {
            Selected = row;

            return;
        }

        // Not drawn yet - a savegame just published is on its way - so the next drawing selects it.
        _selectOnArrival = savegameId;
    }

    public void Dispose()
    {
        _store.Changed -= OnSavegamesChanged;
        _profileStore.Changed -= OnProfilesChanged;
        _bindingStore.BindingsChanged -= OnBindingsChanged;
        _games.GameChanged -= OnFolderStateChanged;
        _driftMonitor.Changed -= OnFolderStateChanged;

        _pageLifetime.Cancel();
        _annotation?.Cancel();

        Savegames.Clear();

        _pageLifetime.Dispose();
    }


    [RelayCommand]
    private Task Refresh()
        => RunAsync("reading the savegames", () => _store.RefreshAsync(_repo.Id, _lifetime));

    /// <summary>
    /// Makes a savegame out of a save that is already on this disk. Here so a repo's first savegame
    /// can be made from the list that is empty and saying so.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishSave()
    {
        IsWorking = true;

        try
        {
            await _publishFlow.PublishAsync(_repo, preselectProfileId: null, Select, _lifetime);
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
            await StartAsync(row, number, SelectedEntry.ProfileRevision, SavegameCheckOutMode.CheckOut, null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyEntry))]
    private async Task TakeCopySnapshot()
    {
        if (Selected is SavegameListItemViewModel row && SelectedEntry?.SnapshotNumber is int number)
        {
            await StartAsync(row, number, SelectedEntry.ProfileRevision, SavegameCheckOutMode.TakeCopy, null);
        }
    }

    /// <summary>
    /// Renames the selected savegame. That is the whole of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing else moves with the name.</b> The snapshots, the claim log, whoever is holding it and
    /// which mod list it follows are all untouched - a savegame cannot be moved between profiles, so
    /// there is no second field this modal could grow. The server's route says the same thing from
    /// its end: it became a rename in Phase 9 and takes nothing but a name.
    /// </para>
    /// <para>
    /// <b>The clash is the server's to find.</b> Names are unique per repo behind a filtered unique
    /// index, so checking here first would be a second copy of a rule that would still be racing
    /// somebody else's rename. Losing that race re-opens the modal with what they typed rather than
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
        var previous = row.Name;

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
                    await _store.WriteAsync(
                        _repo.Id,
                        token => _savegamesClient.UpdateSavegameV1Async(
                            _repo.Id, row.Id, new UpdateSavegameRequest { Name = name, ExpectedVersion = row.Savegame.Version }, token),
                        _lifetime);
                }
                catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.NameTaken)
                {
                    title = "That name is taken";
                    message = $"Something else in this repo is already called '{name}'. Pick another.";
                    suggested = name;

                    continue;
                }
                catch (ApiException<CustomProblemDetails> exception) when (exception.Result.Type is ProblemType.SavegameChanged)
                {
                    // The store has read it again on the refusal, so the next attempt is made against what
                    // somebody else just did.
                    _errorReporter.Record(exception, "renaming a savegame");

                    title = "Somebody else changed it";
                    message = $"It is called '{row.Name}' now. Rename it again, or leave it.";
                    suggested = name;

                    continue;
                }
                finally
                {
                    IsWorking = false;
                }

                _toasts.Show($"'{previous}' is called '{name}' now.");

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

        var confirmation = ConfirmationModalViewModel.ConfirmArchive(row.Name, "savegame");

        await _modalService.Show(confirmation);

        if (confirmation.Result is false)
        {
            return;
        }

        await RunAsync("archiving a savegame", async () =>
        {
            await _store.WriteAsync(_repo.Id, token => _savegamesClient.ArchiveSavegameV1Async(_repo.Id, row.Id, token), _lifetime);
        });
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

        var confirmation = new ConfirmationModalViewModel(
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

        await RunAsync("deleting a savegame snapshot", async () =>
        {
            await _store.WriteAsync(_repo.Id, token => _savegamesClient.DeleteSavegameSnapshotV1Async(_repo.Id, row.Id, number, token), _lifetime);
        });
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

        if (value is null)
        {
            _timelineLoad.Cancel();

            return;
        }

        _ = LoadTimelineAsync(value);
    }


    /// <summary>
    /// Brings the list in line with the savegames the store holds. Rows are kept by id and updated in
    /// place, so the selection and the history beside it survive a read that changed nothing about them.
    /// </summary>
    /// <param name="annotate">
    /// Whether to work the late chips out again. They hash slots on disk, so only a change to the savegames,
    /// the profiles or what this machine holds asks for them - not every drift check.
    /// </param>
    private void Publish(Guid? select, bool annotate)
    {
        var savegames = _store.Live(_repo.Id);
        var ordered = InListOrder(savegames).ToList();
        var wanted = select ?? _selectOnArrival ?? Selected?.Id;

        StatisticsText = DescribeStatistics(SavegameStatistics.From(savegames));

        // Two people called Anton can both hold a save in this repo, and neither of them is the
        // duplicate - so the tag goes on both or on neither, decided over this list.
        var ambiguous = UserDisplay.FindAmbiguous(
            savegames.Select(x => x.Checkout?.User).OfType<UserDto>());

        var rows = Savegames.ToDictionary(x => x.Id);
        var selectedHistoryMoved = false;

        for (var i = Savegames.Count - 1; i >= 0; i--)
        {
            if (ordered.All(x => x.Id != Savegames[i].Id))
            {
                Savegames.RemoveAt(i);
            }
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            var savegame = ordered[index];
            var isAmbiguous = ambiguous.Contains(savegame.Checkout?.User.Id ?? "");

            if (rows.TryGetValue(savegame.Id, out var row))
            {
                selectedHistoryMoved |= row == Selected && HistoryMoved(row.Savegame, savegame);

                row.Update(savegame, isAmbiguous, HolderAvatar(savegame, row));
            }
            else
            {
                row = new SavegameListItemViewModel(
                    savegame, _currentUserId, IsMember, isAmbiguous, _time, HolderAvatar(savegame, null), this);
            }

            var at = Savegames.IndexOf(row);

            if (at == -1)
            {
                Savegames.Insert(index, row);
            }
            else if (at != index)
            {
                Savegames.Move(at, index);
            }
        }

        // One read of the game's folder state for the whole list, rather than one per row: a
        // manifest is every mod in the profile with a hash each, and twenty rows must not cost twenty
        // parses of it. After the rows exist, because a refusal names the savegame in the way - which
        // is a row in this same list.
        var host = _offers.ReadHost(_repo);

        foreach (var row in Savegames)
        {
            _offers.Offer(_repo, row, host, _heldNames);
            row.SetRevisionsBehind(RevisionsBehind(row.Savegame));
        }

        IsEmpty = Savegames.Count == 0;

        var previous = Selected;

        // Assigning a different row is what loads its timeline.
        Selected = Savegames.FirstOrDefault(x => x.Id == wanted) ?? Savegames.FirstOrDefault();

        if (wanted is Guid arrived && Selected?.Id == arrived)
        {
            _selectOnArrival = null;
        }

        if (selectedHistoryMoved && Selected is SavegameListItemViewModel selected && selected == previous)
        {
            _ = LoadTimelineAsync(selected);
        }

        if (annotate)
        {
            _annotation?.Cancel();
            _annotation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
            _ = AnnotateAsync([.. Savegames], _annotation.Token);
        }
    }

    /// <summary>Whether what the history column shows for a savegame can have changed.</summary>
    private static bool HistoryMoved(SavegameDto before, SavegameDto after)
        => before.Head?.Number != after.Head?.Number
            || before.SnapshotCount != after.SnapshotCount
            || before.Checkout?.Id != after.Checkout?.Id
            || before.Checkout?.Status != after.Checkout?.Status;

    /// <summary>The holder's avatar, kept where the holder has not changed so its picture is not loaded again.</summary>
    private AvatarViewModel? HolderAvatar(SavegameDto savegame, SavegameListItemViewModel? row)
    {
        if (savegame.Checkout?.User is not UserDto holder)
        {
            return null;
        }

        return row?.Savegame.Checkout?.User.Id == holder.Id && row.HolderAvatar is AvatarViewModel kept
            ? kept
            : _avatarFactory.Create(holder);
    }

    /// <summary>
    /// Draws the list again from what the store holds, after anything it is worked out from changed:
    /// the savegames, the profiles, or what this machine holds and where its mod folder is.
    /// </summary>
    /// <remarks>
    /// A newer redraw supersedes an older one still reading, so an annotation asked for is carried over
    /// until a redraw lands rather than lost with the one that asked.
    /// </remarks>
    private Task RepublishAsync(bool annotate)
    {
        _annotationDue |= annotate;

        return _publishLoad.RunAsync(
            ReadHeldNamesAsync,
            names =>
            {
                var due = _annotationDue;
                _annotationDue = false;

                _heldNames = names;
                Publish(select: null, due);
            },
            exception =>
            {
                _errorReporter.Record(exception, "drawing the savegame list again");
                _problems.Report(BackgroundProblem.DeferredLoad);

                return Task.CompletedTask;
            });
    }

    private void OnSavegamesChanged(Guid repoId)
    {
        // Any repo: a savegame this game holds from another one is named on the rows here.
        _ = RepublishAsync(annotate: true);
    }

    private void OnProfilesChanged(Guid repoId)
    {
        if (repoId == _repo.Id)
        {
            _ = RepublishAsync(annotate: true);
        }
    }

    /// <summary>Raised on whichever thread changed it, so the redraw is posted.</summary>
    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(() => RepublishAsync(annotate: true));
    }

    /// <summary>Which profile the game follows and what its folders hold, which decide every row'"'"'s offer.</summary>
    private void OnFolderStateChanged(object? sender, EventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(() => RepublishAsync(annotate: false));
    }

    /// <summary>
    /// A profile's savegames together, the one played most recently first. Savegames that follow no
    /// mod list have no group to sit in, so they come last.
    /// </summary>
    private static IEnumerable<SavegameDto> InListOrder(IEnumerable<SavegameDto> savegames)
        => savegames
            .OrderBy(x => x.ProfileId is null)
            .ThenBy(SavegameWording.ProfileOf, NaturalOrder.Comparer)
            .ThenBy(x => x.ProfileId)
            .ThenByDescending(x => x.Head?.Created)
            .ThenBy(x => x.Name, NaturalOrder.Comparer)
            .ThenBy(x => x.Id);

    /// <summary>How many revisions the profile has moved on since the save was last played. Zero where it has not, or cannot be told.</summary>
    private int RevisionsBehind(SavegameDto savegame)
        => savegame.Head?.ProfileRevision is int played
            && _profileStore.Find(_repo.Id, savegame.ProfileId) is Profile profile
            && profile.HeadRevision > played
                ? profile.HeadRevision - played
                : 0;

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

    /// <summary>What the savegames this repo's game holds are called, whichever repo each is in.</summary>
    private async Task<IReadOnlyDictionary<Guid, HeldSavegameName>> ReadHeldNamesAsync(CancellationToken cancellationToken)
        => _repo.Games.FirstOrDefault() is Game game
            ? await _heldSavegameNames.ReadAsync(game, _repo.Id, cancellationToken)
            : new Dictionary<Guid, HeldSavegameName>();

    /// <summary>
    /// The two chips that are not facts about the savegame: whether a slot on <em>this</em> machine has
    /// moved, and how far behind the save's revision is. Both are appended as they arrive rather than
    /// holding up a list that is otherwise ready, and both are absorbed on failure - a missing chip
    /// costs a caption, and the row is still correct without it.
    /// </summary>
    private async Task AnnotateAsync(IReadOnlyList<SavegameListItemViewModel> rows, CancellationToken cancellationToken)
    {
        try
        {
            await AnnotateRevisionsAsync(rows, cancellationToken);
            await AnnotateUnpublishedPlayAsync(rows, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing awaits this, so an escape would go unobserved rather than reaching the shell.
            // Logged and left to the notice column rather than raised as a modal: the list is
            // correct without these chips, and a modal over a list that loaded fine is the wrong size
            // of answer for a missing caption.
            _errorReporter.Record(exception, "checking the savegame list against this machine");
            _problems.Report(BackgroundProblem.DeferredLoad);
        }
    }

    private async Task AnnotateRevisionsAsync(IReadOnlyList<SavegameListItemViewModel> rows, CancellationToken cancellationToken)
    {
        if (_repo.Adapter.FindSavegameCompatibility() is not SavegameCompatibilityPolicy policy)
        {
            return;
        }

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (row.Savegame.Head?.ProfileRevision is not int played ||
                _profileStore.Find(_repo.Id, row.Savegame.ProfileId) is not Profile profile)
            {
                row.SetCompatibility(false);

                continue;
            }

            try
            {
                var verdict = await _compatibilityCheck.AssessAsync(
                    _repo.Id, profile.Id, played, profile.HeadRevision, policy, cancellationToken);

                row.SetCompatibility(verdict?.ShouldPrompt is true);
            }
            catch (ApiException exception)
            {
                // Only the chip's colour depends on this, so the row stays as it is. The check-out
                // asks again and does not go ahead without an answer.
                _errorReporter.Record(exception, "comparing a savegame's revision with its profile's latest");
            }
        }
    }

    private async Task AnnotateUnpublishedPlayAsync(IReadOnlyList<SavegameListItemViewModel> rows, CancellationToken cancellationToken)
    {
        var played = new HashSet<Guid>();

        foreach (var game in _repo.Games.ToList())
        {
            // A hold whose folder the settings no longer name has nothing to hash, so there is
            // nothing this chip could say about it. The row says that state in its own words instead,
            // and offers the one action it has - see SavegameListItemViewModel.HoldNote.
            var unreachable = _holds.GetUnreachableHolds(game)
                .Select(x => x.SavegameId)
                .ToHashSet();

            foreach (var binding in _bindingStore.GetBindings(game.Identity))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (rows.FirstOrDefault(x => x.Id == binding.SavegameId) is not SavegameListItemViewModel row
                    || unreachable.Contains(binding.SavegameId))
                {
                    continue;
                }

                var availability = await _slots.ClassifySlotAsync(game, binding.Slot, cancellationToken);

                if (availability is SavegameSlotAvailability.HeldWithUnpublishedPlay)
                {
                    played.Add(row.Id);
                }
            }
        }

        // Every row, so one checked in since the last pass loses the chip.
        foreach (var row in rows)
        {
            row.SetUnpublishedPlay(played.Contains(row.Id));
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
    private Task LoadTimelineAsync(SavegameListItemViewModel row)
        => _timelineLoad.RunAsync(
            async token => (
                Snapshots: await _savegamesClient.GetSavegameSnapshotsV1Async(_repo.Id, row.Id, null, null, token),
                Checkouts: await _savegamesClient.GetSavegameCheckoutsV1Async(_repo.Id, row.Id, null, null, token)),
            history => ShowTimeline(history.Snapshots, history.Checkouts),
            exception =>
            {
                Timeline.Clear();

                return _errorReporter.ShowAsync(exception, $"reading the history of '{row.Name}'");
            });

    private void ShowTimeline(GetSavegameSnapshotsResponse snapshots, GetSavegameCheckoutsResponse checkouts)
    {
        // Newest first, and the rank behind it carries weight rather than tidying: publishing,
        // checking in and taking a save over can each write two rows off one clock reading, so the
        // moment alone leaves the tie to whichever read was concatenated first - which is what put
        // a publish above the claim it opened and made the save look checked out before it existed.
        var now = _time.GetUtcNow();

        var entries = snapshots.Snapshots
            .Select(x => SavegameTimelineEntryViewModel.ForSnapshot(x, x.Number == snapshots.HeadSnapshot, now))
            .Concat(checkouts.Checkouts.Select(x => SavegameTimelineEntryViewModel.ForClaimTaken(x, now)))
            .Concat(SavegameTimelineRules.EndingsToShow(snapshots.Snapshots, checkouts.Checkouts)
                .Select(x => SavegameTimelineEntryViewModel.ForClaimEnded(x, now)))
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


    Task ISavegameRowActions.CheckOutAsync(SavegameListItemViewModel row, SavegameRevisionMode? revisionMode)
        => StartAsync(row, row.Savegame.Head?.Number ?? 0, row.Savegame.Head?.ProfileRevision, SavegameCheckOutMode.CheckOut, revisionMode);

    Task ISavegameRowActions.TakeCopyAsync(SavegameListItemViewModel row, SavegameRevisionMode? revisionMode)
        => StartAsync(row, row.Savegame.Head?.Number ?? 0, row.Savegame.Head?.ProfileRevision, SavegameCheckOutMode.TakeCopy, revisionMode);

    /// <summary>
    /// Hands a save back from the game holding it, without going to that game's own page.
    /// </summary>
    /// <remarks>
    /// <b>The game is the row's, not a choice.</b> A check-in uploads what is in a slot, so the
    /// only game it can mean is the one whose slot holds the copy - which is why this reads
    /// <see cref="SavegameListItemViewModel.HeldHere"/> and not <c>Host</c>, and why there is no
    /// picker here the way there is for a check-out.
    /// </remarks>
    Task ISavegameRowActions.CheckInAsync(SavegameListItemViewModel row)
        => row.HeldHere is Game game
            ? RunAsync("checking a savegame in", () => _checkInFlow.CheckInHeldAsync(game, row.Id, row.Name, _lifetime))
            : Task.CompletedTask;

    /// <summary>
    /// Gives a save back without minting a snapshot - taken by mistake, never played.
    /// </summary>
    Task ISavegameRowActions.DiscardAsync(SavegameListItemViewModel row)
    {
        if (row.Hold is not SavegameHoldHere hold)
        {
            return Task.CompletedTask;
        }

        return RunAsync("giving a savegame back", async () =>
        {
            // Asked of the disk here rather than read off the row's chip. The chip arrives from a
            // background pass that may not have reached this row yet, and the two confirmations this
            // decides between are "nothing is lost" and "an evening of play goes to the Recycle Bin".
            var played = await _slots.ClassifySlotAsync(hold.Game, hold.Slot, _lifetime)
                is SavegameSlotAvailability.HeldWithUnpublishedPlay;

            // The savegame's name where the modal wants a slot label, as the check-in does: a slot
            // id is a folder name the player has never thought in, and what they are giving back is
            // the save rather than the folder.
            if (await _checkInFlow.DiscardAsync(hold.Game, row.Id, row.Name, row.Name, played, _lifetime) is false)
            {
                return;
            }

            await _driftMonitor.CheckAsync();
        });
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
    Task ISavegameRowActions.DisconnectAsync(SavegameListItemViewModel row)
    {
        if (row.Hold is not SavegameHoldHere hold)
        {
            return Task.CompletedTask;
        }

        return RunAsync("disconnecting a savegame", async () =>
        {
            if (await _disconnectFlow.DisconnectAsync(hold.Game, row.Id, row.Name, hold.FolderName) is false)
            {
                return;
            }

            _toasts.Show($"ModsDude has stopped tracking '{row.Name}'. The save is still on this disk, and the claim is still yours.");

            await _driftMonitor.CheckAsync();
        });
    }

    /// <summary>
    /// Runs one of the page's actions with the list marked busy, and says a failure once, here.
    /// </summary>
    /// <param name="doing">What was being done, for the error modal: "archiving a savegame".</param>
    private async Task RunAsync(string doing, Func<Task> work)
    {
        IsWorking = true;

        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // Navigated away. There is no page left to report on.
        }
        catch (Exception exception)
        {
            await _errorReporter.ShowAsync(exception, doing);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private Task StartAsync(
        SavegameListItemViewModel row,
        int snapshotNumber,
        int? playedRevision,
        SavegameCheckOutMode mode,
        SavegameRevisionMode? revisionMode)
        => RunAsync("checking a savegame out", () => _checkOutFlow.CheckOutAsync(
            _repo, row.Savegame, snapshotNumber, playedRevision, mode, revisionMode, _currentUserId, _heldNames, _lifetime));


    public class Factory(IServiceProvider serviceProvider)
    {
        /// <param name="select">The savegame to arrive with selected - see <see cref="SelectOnArrival"/>.</param>
        public RepoSavegamesPageViewModel Create(Repo repo, Guid? select = null)
        {
            var page = ActivatorUtilities.CreateInstance<RepoSavegamesPageViewModel>(serviceProvider, repo);
            page.SelectOnArrival(select);

            return page;
        }
    }
}
