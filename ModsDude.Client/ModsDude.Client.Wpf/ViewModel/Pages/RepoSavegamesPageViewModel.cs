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
    private readonly GameRepository _gameRepository;
    private readonly ProfileApplyService _applyService;
    private readonly ModSyncService _syncService;
    private readonly InstanceDriftMonitor _driftMonitor;
    private readonly SavegameFlowService _flowService;
    private readonly SyncManifestStore _manifestStore;
    private readonly ShellNavigationService _shellNavigation;
    private readonly IModalService _modalService;
    private readonly IErrorReporter _errorReporter;
    private readonly IBackgroundTaskReporter _backgroundTasks;

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
        GameRepository gameRepository,
        ProfileApplyService applyService,
        ModSyncService syncService,
        InstanceDriftMonitor driftMonitor,
        SavegameFlowService flowService,
        SyncManifestStore manifestStore,
        ShellNavigationService shellNavigation,
        IModalService modalService,
        IErrorReporter errorReporter,
        IBackgroundTaskReporter backgroundTasks,
        bool showPastSavegames = false)
    {
        _backgroundTasks = backgroundTasks;
        _manifestStore = manifestStore;
        _showPastSavegames = showPastSavegames;
        _repo = repo;
        _savegamesClient = savegamesClient;
        _savegameService = savegameService;
        _headVersions = headVersions;
        _bindingStore = bindingStore;
        _profileService = profileService;
        _currentUserService = currentUserService;
        _gameRepository = gameRepository;
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

        IsMember = repo.MembershipLevel >= RepoMembershipLevel.Member;

        // Admin, like pruning a profile's revisions and for the same reason: it destroys a backup,
        // which is not part of running a repo.
        CanPruneVersions = repo.MembershipLevel >= RepoMembershipLevel.Admin;

        Savegames = [];
        Timeline = [];
    }


    public string RepoName => _repo.Name;

    /// <summary>
    /// Whether this user may take a claim at all. Reading the list and copying a version is not gated.
    /// </summary>
    /// <remarks>
    /// The coarse half of the answer. Whether a particular savegame can be taken <em>here and now</em>
    /// is the row's <see cref="SavegameListItemViewModel.CanCheckOut"/>, which also knows what this
    /// machine is holding and where its mod folder is.
    /// </remarks>
    public bool IsMember { get; }

    /// <summary>Whether deleting a version of a savegame's history is on offer. Admin only.</summary>
    public bool CanPruneVersions { get; }

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
        : "No saves here yet. Publish one from a game installation's Saves tab, and it appears in this list for everybody.";

    /// <summary>Versions and checkouts as one column, newest first.</summary>
    public ObservableCollection<SavegameTimelineEntryViewModel> Timeline { get; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveSavegameCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameSavegameCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(RenameSavegameCommand))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveSavegameCommand))]
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
    /// Renames the selected savegame. That is the whole of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing else moves with the name.</b> The versions, the claim log, whoever is holding it and
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
        var message = "What everybody else in this repo will see it called. Its versions, its history and "
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

                Status = $"'{row.Name}' is called '{name}' now.";

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
    private bool CanArchiveSelected() => IsMember && IsWorking is false && Selected is not null;

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

    private bool CanActOnEntry() => IsMember && IsWorking is false && SelectedEntry is { IsVersion: true };
    private bool CanCopyEntry() => IsWorking is false && SelectedEntry is { IsVersion: true };

    /// <summary>
    /// Into the profile's own history, where two revisions can be compared properly. The comparison
    /// already exists; repeating a cut-down version of it here would be a second answer to maintain.
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

        // Past is a fact about which savegame a profile is following, so the toggle hides rows rather than
        // marking them differently - and the count is said out loud, because a filter nobody can see
        // is a list that is quietly wrong.
        var shown = savegames.Where(x => ShowPastSavegames || x.SupersededAt is null).ToList();

        HiddenPastCount = savegames.Count - shown.Count;

        // Two people called Anton can both hold a save in this repo, and neither of them is the
        // duplicate - so the tag goes on both or on neither, decided over this list.
        var ambiguous = UserDisplay.FindAmbiguous(
            savegames.Select(x => x.Checkout?.User).OfType<UserDto>());

        // One read of each game's folder state for the whole list, rather than one per row: a
        // manifest is every mod in the profile with a hash each, and twenty rows must not cost twenty
        // parses of it.
        var hosts = ReadHosts();

        foreach (var savegame in shown.OrderBy(x => x.Name, NaturalOrder.Comparer))
        {
            var row = new SavegameListItemViewModel(
                savegame,
                FindProfile(savegame.ProfileId)?.Name ?? "A profile you cannot see",
                _currentUserId,
                IsMember,
                ambiguous.Contains(savegame.Checkout?.User.Id ?? ""));

            row.CheckOutRequested += OnCheckOutRequested;
            row.CheckInRequested += OnCheckInRequested;
            row.TakeCopyRequested += OnTakeCopyRequested;
            row.ApplyProfileRequested += OnApplyProfileRequested;
            row.MakeCurrentRequested += OnMakeCurrentRequested;

            Savegames.Add(row);
        }

        // After the rows exist, because a refusal names the savegame in the way - which is a row in
        // this same list.
        foreach (var row in Savegames)
        {
            Offer(row, hosts);
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
            row.CheckInRequested -= OnCheckInRequested;
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
    /// Every game this repo offers, with what it is holding and what its mod folder was last
    /// synced to - the two facts <see cref="SavegameRowRules.Describe"/> needs about a host.
    /// </summary>
    private IReadOnlyList<SavegameHost> ReadHosts()
    {
        var hosts = new List<SavegameHost>();

        foreach (var game in _repo.Games)
        {
            var manifest = _manifestStore.TryRead(game.Id);

            hosts.Add(new SavegameHost(
                game,
                _bindingStore.GetBindings(game.Id),
                manifest?.ProfileId,
                manifest?.ProfileRevision));
        }

        return hosts;
    }

    /// <summary>
    /// Tells a row which game its two buttons act on and what they can do there.
    /// </summary>
    /// <remarks>
    /// <b>The game that could host it now wins.</b> That is the whole question the row is
    /// answering - the buttons either work or they carry a sentence saying what would make them work -
    /// and choosing one that refuses while another would accept turns a one-click evening into a
    /// puzzle. Failing that, the one already following this save's profile, whose folder is the
    /// closest to right and which is the likeliest target anyway; failing that the first, whose
    /// refusal is at least about a folder that exists.
    /// </remarks>
    private void Offer(SavegameListItemViewModel row, IReadOnlyList<SavegameHost> hosts)
    {
        // Answered across every host and before the loop below, which returns the moment it finds an
        // game that would accept a check-out. Where the copy is is not that question and cannot
        // share its search: the game holding a save is routinely the one a check-out likes least,
        // since the folder it is on belongs to the save already in it.
        row.SetHeldHere(hosts
            .FirstOrDefault(x => x.Held.Any(y => y.SavegameId == row.Id))
            ?.Game);

        if (hosts.Count == 0)
        {
            row.SetOffer(SavegameRowRules.Describe(
                row.Id, row.Savegame.ProfileId, null, row.PinnedRevision, [], null, null, hasGame: false),
                null);

            return;
        }

        var head = FindProfile(row.Savegame.ProfileId)?.HeadRevision;

        SavegameHost? fallback = null;
        SavegameRowOffer? fallbackOffer = null;

        foreach (var host in hosts)
        {
            var offer = SavegameRowRules.Describe(
                row.Id,
                row.Savegame.ProfileId,
                head,
                row.PinnedRevision,
                host.Held,
                host.AppliedProfileId,
                host.AppliedRevision,
                hasGame: true);

            if (offer.CanCheckOut)
            {
                row.SetOffer(offer, null);
                row.Host = host.Game;

                return;
            }

            var follows = row.Savegame.ProfileId is Guid profileId
                && host.Game.ActiveProfile == new ActiveProfile(_repo.Id, profileId);

            if (fallbackOffer is null || follows)
            {
                fallback = host;
                fallbackOffer = offer;
            }
        }

        row.SetOffer(fallbackOffer!, NameOfHeld(fallbackOffer!.BlockingSavegameId));
        row.Host = fallback!.Game;
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
            Status ??= $"Some of this list could not be checked against your machine: {exception.Message}";
        }
    }

    private async Task AnnotateRevisionsAsync(IReadOnlyList<SavegameListItemViewModel> rows)
    {
        foreach (var row in rows)
        {
            _lifetime.ThrowIfCancellationRequested();

            if (row.Savegame.Head is not SavegameVersionDto head ||
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
                await LockedPinMovedAsync(profile.Id, played, profile.HeadRevision));
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
        foreach (var game in _repo.Games.ToList())
        {
            foreach (var binding in _bindingStore.GetBindings(game.Id))
            {
                _lifetime.ThrowIfCancellationRequested();

                if (rows.FirstOrDefault(x => x.Id == binding.SavegameId) is not SavegameListItemViewModel row)
                {
                    continue;
                }

                var availability = await _savegameService.ClassifySlotAsync(
                    game, new SavegameSlotId(binding.SlotId), _lifetime);

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

            // Which claims a version already speaks for. Those get no ending row of their own - the
            // version minted against a claim is the check-in, and a thin "Checked back in" at the same
            // second would only say it again. Taken from the loaded window rather than from the whole
            // history on purpose: a check-in whose version has since been pruned, or scrolled past,
            // then gets its ending drawn, which is the point of the claim log outliving the blobs.
            var recorded = versions.Versions
                .Where(x => x.CheckoutId is not null)
                .Select(x => x.CheckoutId!.Value)
                .ToHashSet();

            // Newest first, and the rank behind it carries weight rather than tidying: publishing,
            // checking in and taking a save over each write two rows off one clock reading, so the
            // moment alone leaves the tie to whichever read was concatenated first - which is what put
            // a publish above the claim it opened and made the save look checked out before it existed.
            var entries = versions.Versions
                .Select(x => SavegameTimelineEntryViewModel.ForVersion(x, x.Number == versions.HeadVersion))
                .Concat(checkouts.Checkouts.Select(SavegameTimelineEntryViewModel.ForClaimTaken))
                .Concat(checkouts.Checkouts
                    .Where(x => x.EndedAt is not null && recorded.Contains(x.Id) is false)
                    .Select(SavegameTimelineEntryViewModel.ForClaimEnded))
                .OrderByDescending(x => x.Moment)
                .ThenByDescending(x => x.Rank)
                .ThenByDescending(x => x.VersionNumber);

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
            // The savegame's name where the dialog wants a slot label, as CheckInBlockingAsync does:
            // the slot's own id is a folder name the player has never thought in, and what they are
            // handing back is the save rather than the folder.
            var outcome = await _flowService.CheckInAsync(game, row.Id, row.Name, row.Name, _lifetime);

            if (outcome.WasDeferred)
            {
                Status = $"Left as it is. Your copy of '{row.Name}' is still in its slot and still yours.";

                return;
            }

            if (outcome.Succeeded is false)
            {
                return;
            }

            Status = outcome.KeptPlaying
                ? $"Version {outcome.Version!.Number} of '{row.Name}' is on the server. The save is still in '{game.Name}' and still yours."
                : $"Version {outcome.Version!.Number} of '{row.Name}' is on the server, and the save is anybody's to take.";

            await _driftMonitor.CheckAsync();
            await ReloadAsync(row.Id);
        }
        catch (OperationCanceledException)
        {
            // Navigated away mid-upload. The check-in either landed or it did not, and the next read
            // of this page says which - there is no page left to say it on now.
        }
        catch (Exception exception)
        {
            // The rows raise plain events rather than running commands, so a failure here has no
            // command to carry it to the global handler and has to reach the user itself.
            await _errorReporter.ShowAsync(exception, "checking a savegame in");
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

            Status = result.Superseded is SavegameDto displaced
                ? $"'{row.Name}' is {row.ProfileName}'s current savegame and follows it from here. '{displaced.Name}' is past - still playable, and its mod list no longer moves."
                : $"'{row.Name}' is {row.ProfileName}'s current savegame and follows it from here.";

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
            || row.Host is not Game game
            || FindProfile(row.Savegame.ProfileId) is not ProfileDto profile)
        {
            return;
        }

        IsWorking = true;

        try
        {
            var outcome = await _applyService.ApplyAsync(
                _repo,
                game,
                profile.Id,
                profile.Name,
                confirmPlan: false,
                progress: null,
                _lifetime,
                revision: row.PinnedRevision ?? profile.HeadRevision);

            RecordActiveProfile(game, profile, outcome.RecordsIntent);

            Status = outcome.Message;

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

        var games = _repo.Games.ToList();

        if (games.Count == 0)
        {
            await _modalService.Show(ConfirmationDialogViewModel.Refusal(
                "No game is connected here",
                $"A savegame has to be written into an installation of the game. Use 'Connect game' in {_repo.Name} first."));

            return;
        }

        IsWorking = true;

        try
        {
            // The one the row's buttons were about, so the dialog opens on the folder the row just
            // described. Choosing again here is how a row comes to say "ready" about one game and
            // open a dialog about another.
            var preferred = row.Host ?? games[0];

            var context = await BuildContextAsync(row, preferred, mode, _lifetime);

            var modal = new SavegameCheckOutModalViewModel(
                mode,
                row.Name,
                row.ProfileName,
                versionNumber,
                row.Savegame.Head.Number,
                games,
                context,
                (game, cancellationToken) => BuildContextAsync(row, game, mode, cancellationToken));

            await _modalService.Show(modal);

            if (modal.CheckInFirstSavegameId is Guid blocking)
            {
                await CheckInBlockingAsync(modal.SelectedGame, blocking, row, versionNumber, mode);

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
        Game? game,
        Guid blockingSavegameId,
        SavegameListItemViewModel row,
        int versionNumber,
        SavegameCheckOutMode mode)
    {
        if (game is null)
        {
            return;
        }

        var blocking = Savegames.FirstOrDefault(x => x.Id == blockingSavegameId);

        var outcome = await _flowService.CheckInAsync(
            game,
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
        // Downloading and unpacking a save is the slow half of both verbs, and both are safe to walk
        // away from - the claim, where there is one, is taken before the bytes move.
        using var task = _backgroundTasks.Begin(
            mode is SavegameCheckOutMode.TakeCopy
                ? $"Copying '{row.Name}' into '{result.Game.Name}'"
                : $"Checking '{row.Name}' out into '{result.Game.Name}'",
            $"Version {versionNumber}");

        if (mode is SavegameCheckOutMode.TakeCopy)
        {
            await _savegameService.TakeCopyAsync(result.Game, row.Savegame, versionNumber, result.Slot.Id, _lifetime);

            Status = $"Version {versionNumber} of '{row.Name}' is in '{result.Game.Name}'. Nobody was stopped from playing it, " +
                     "and this machine holds no claim on it - the slot is an ordinary save of your own now.";

            return;
        }

        var savegame = row.Savegame;

        // Restoring copies forward, so an old version becomes the head and the check-out that follows
        // has no stale base to reason about. Nothing in between is deleted.
        if (versionNumber != savegame.Head?.Number)
        {
            task.Report($"Restoring version {versionNumber} as the newest one");

            await _savegamesClient.RestoreSavegameVersionV1Async(
                _repo.Id, savegame.Id, versionNumber, new RestoreSavegameVersionRequest(), _lifetime);

            var refreshed = await _savegamesClient.GetSavegamesV1Async(_repo.Id, _lifetime);

            savegame = refreshed.FirstOrDefault(x => x.Id == savegame.Id) ?? savegame;
        }

        task.Report("Writing it into the slot");

        await _savegameService.CheckOutAsync(result.Game, savegame, result.Slot.Id, _lifetime);

        Status = $"'{row.Name}' is checked out to you, in '{result.Game.Name}'.";

        await ApplyProfileAsync(result.Game, savegame);

        await ReloadAsync(row.Id);
    }

    /// <summary>
    /// The mod half, last and separately. Checking out a save derives and applies its profile where the
    /// adapter has mods, and is simply "write the slot" where it does not - and a user who wanders off
    /// after the claim still holds the save and has it on disk.
    /// </summary>
    /// <remarks>
    /// <b>Which revision is not decided here.</b> The binding was written a moment ago and carries what
    /// this savegame runs on - head for the profile's current savegame, its own pinned revision for a past
    /// one - and every apply resolves it from there. Working it out a second time in this method is how
    /// the check-out comes to install a different list from the one the drift check then expects.
    /// </remarks>
    private async Task ApplyProfileAsync(Game game, SavegameDto savegame)
    {
        if (_repo.Adapter.CanSupportMods is false)
        {
            return;
        }

        if (FindProfile(savegame.ProfileId) is not ProfileDto profile)
        {
            return;
        }

        var plan = await _applyService.TryPlanAsync(_repo, game, profile.Id, profile.Name, revision: null, _lifetime);

        if (plan is null)
        {
            Status += $" '{game.Name}' could not be reached, so its mod folder was left as it is.";

            return;
        }

        // Nothing unrecognised in the folder means there is nothing to disclose, and the ordinary night
        // stays one click.
        if (plan.Unrecognised.Count == 0)
        {
            var outcome = await _applyService.ApplyAsync(
                _repo, game, profile.Id, profile.Name, confirmPlan: false, progress: null, _lifetime);

            RecordActiveProfile(game, profile, outcome.RecordsIntent);

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
            // Review leaves the game drifted, and the persistent notification takes it from there -
            // the same answer this design gives for a game that cannot be applied to right now.
            RecordActiveProfile(game, profile, true);

            Status += " The mod folder was left as it is until you decide what to keep.";

            await _driftMonitor.CheckAsync();
            await _shellNavigation.GoToProfileModsAsync(_repo.Id, profile.Id, game.Id);

            return;
        }

        var result = await _syncService.ExecuteAsync(plan, null, _lifetime);

        RecordActiveProfile(game, profile, true);

        Status += result.Completed
            ? $" '{game.Name}' now matches '{profile.Name}'."
            : $" {result.Failures.Count} mods could not be applied to '{game.Name}'.";

        await _driftMonitor.CheckAsync();
    }

    /// <summary>
    /// The standing intent is recorded even where the folder could not be put right: the game is
    /// still meant to follow this profile, and being left drifted is what the notice is for.
    /// </summary>
    private void RecordActiveProfile(Game game, ProfileDto profile, bool record)
    {
        if (record)
        {
            _gameRepository.SetActiveProfile(game, new ActiveProfile(_repo.Id, profile.Id));
        }
    }

    /// <summary>
    /// Everything the dialog needs about one game: its slots and their safety, what the mod folder
    /// would have to do, and how far the save's revision is from the profile's.
    /// </summary>
    private async Task<SavegameCheckOutContext> BuildContextAsync(
        SavegameListItemViewModel row,
        Game game,
        SavegameCheckOutMode mode,
        CancellationToken cancellationToken)
    {
        var slots = await _savegameService.GetSlotsAsync(game, cancellationToken);
        var options = new List<SavegameSlotOptionViewModel>();

        foreach (var slot in slots)
        {
            var availability = await _savegameService.ClassifySlotAsync(game, slot.Id, cancellationToken);
            var binding = _bindingStore.GetBindingForSlot(game.Id, slot.Id);

            options.Add(new SavegameSlotOptionViewModel(
                slot,
                availability,
                binding?.SavegameId,
                binding is SavegameCheckoutBinding held
                    ? Savegames.FirstOrDefault(x => x.Id == held.SavegameId)?.Name
                    : null));
        }

        var suggested = await _savegameService.SuggestSlotAsync(game, row.Id, cancellationToken);
        var hint = _bindingStore.GetSlotHint(game.Id, row.Id);

        return new SavegameCheckOutContext(
            game,
            options,
            suggested,
            DescribeSuggestion(options, suggested, hint),
            mode is SavegameCheckOutMode.CheckOut
                ? await BuildModsSummaryAsync(row, game, cancellationToken)
                : null,
            await BuildRevisionNoteAsync(row),
            // Absent for a copy, which applies nothing: the slot is written and the mod folder is left
            // exactly as it was, so there is no list the save is about to run on.
            mode is SavegameCheckOutMode.CheckOut ? DescribeRunsOn(row) : null);
    }

    /// <summary>
    /// Which revision the folder will be on afterwards, in one line.
    /// </summary>
    /// <remarks>
    /// <b>Worth showing even for a current savegame</b>, where the number can differ from the one the
    /// savegame was last played on whenever anybody has edited the profile since - and that is precisely
    /// the case where somebody wants to have seen the number before the evening rather than after it.
    /// </remarks>
    private string? DescribeRunsOn(SavegameListItemViewModel row)
    {
        if (row.PinnedRevision is int pinned)
        {
            return $"This savegame stays on rev {pinned}. Playing it does not move it forward.";
        }

        return FindProfile(row.Savegame.ProfileId) is ProfileDto profile
            ? $"Will run on {profile.Name} rev {profile.HeadRevision}."
            : null;
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
                ? "This game reports no savegame slots at all."
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
        Game game,
        CancellationToken cancellationToken)
    {
        if (_repo.Adapter.CanSupportMods is false || FindProfile(row.Savegame.ProfileId) is not ProfileDto profile)
        {
            return null;
        }

        // Named rather than resolved from the game: nothing is holding this savegame yet, so the
        // game has no opinion about it - and the plan shown here has to be the plan that runs.
        var plan = await _applyService.TryPlanAsync(
            _repo, game, profile.Id, profile.Name, SavegameService.TargetRevisionOf(row.Savegame), cancellationToken);

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
            head.ProfileRevision is not int played ||
            FindProfile(row.Savegame.ProfileId) is not ProfileDto profile ||
            profile.HeadRevision <= played)
        {
            return null;
        }

        var moved = await LockedPinMovedAsync(profile.Id, played, profile.HeadRevision);

        var text = $"Last played on revision {played}; {profile.Name} is now at {profile.HeadRevision}.";

        return new SavegameRevisionNote(
            moved
                ? text + " A locked mod moved between them, and hosting this save on it may damage it."
                : text,
            moved);
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


    /// <summary>
    /// One game this repo offers, with the two things a row's buttons turn on: what it is holding,
    /// and which revision of which profile its mod folder was last made to match.
    /// </summary>
    private sealed record SavegameHost(
        Game Game,
        IReadOnlyList<SavegameCheckoutBinding> Held,
        Guid? AppliedProfileId,
        int? AppliedRevision);


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
