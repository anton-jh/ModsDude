using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Notices;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Updates;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Wpf.Diagnostics;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The column down the right-hand side: everything the app has to say that no page owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>A list where there used to be one card.</b> The shell drew a single drift notice in the bottom
/// corner that multiplexed the mod half, the locked half, the savegame half and the shared mod cache
/// into one bordered box, showed the first drifted game and summarised the rest as
/// <em>3 games have drifted</em>, and closed the lists it could not fit with
/// <em>2 more savegame problems here as well</em>. Every one of those was a list item flattened into
/// a sentence because there was only one card to put it in.
/// </para>
/// <para>
/// <b>The old shape argued against this, and is being overruled deliberately.</b> It said that "two
/// notices racing to say one each is how a warning becomes noise" and that "a person acts on one
/// problem at a time". Both are true of a corner with room for one card. They stop being true of a
/// list that sorts by what is at stake, keeps a game's folders together, opens only what is
/// critical and holds the tail behind a count. If this column ever reads as a wall, the corner was
/// right and this should go back.
/// </para>
/// <para>
/// <b>It never stops the user working.</b> Same rule the corner had, and for the same reason: the
/// drift already happened while they were in the game, and the background failures are things the
/// app chose to absorb. Nothing here is a modal, and the background-task strip stays along the top
/// edge rather than joining this list - it is about the present and offers nothing, and a progress
/// bar among actionable warnings would make the column mean two things.
/// </para>
/// </remarks>
public partial class NoticeCenterViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How many cards are drawn before the rest go behind a count. Six is about what fits beside a
    /// page on a laptop without the column becoming the page.
    /// </summary>
    private const int MaxVisible = 6;

    private readonly DriftMonitor _monitor;
    private readonly RepoRepository _repoRepository;
    private readonly GameRepository _gameRepository;
    private readonly ProfileService _profileService;
    private readonly SavegameBindingStore _bindingStore;
    private readonly ProfileApplyService _applyService;
    private readonly ShellNavigationService _navigation;
    private readonly INoticeEnvironment _environment;
    private readonly DismissalLedger _dismissals;
    private readonly BackgroundProblemSource _problems;
    private readonly IUpdateStatus _updates;
    private readonly ConnectionRetry _connection;
    private readonly ILogger _logger;

    /// <summary>The one place a notice is suppressed: the drifted profile's own mod list editor.</summary>
    private readonly HashSet<ActiveProfile> _suppressed = [];

    private readonly Dictionary<string, NoticeViewModel> _cards = [];

    /// <summary>
    /// Every notice key this session has already shown the user, so that a build introducing one they
    /// have not seen can open the column and a rebuild of the same set cannot.
    /// </summary>
    /// <remarks>
    /// <b>Not the same as the cards.</b> Those are only what is currently drawn, which is capped and
    /// filtered by dismissal; a notice waved away is still one the user has seen, and re-raising it
    /// under the same signature must not re-open the column they closed.
    /// </remarks>
    private readonly HashSet<string> _seen = [];


    public NoticeCenterViewModel(
        DriftMonitor monitor,
        RepoRepository repoRepository,
        GameRepository gameRepository,
        ProfileService profileService,
        SavegameBindingStore bindingStore,
        ProfileApplyService applyService,
        ShellNavigationService navigation,
        INoticeEnvironment environment,
        DismissalLedger dismissals,
        BackgroundProblemSource problems,
        IUpdateStatus updates,
        ConnectionRetry connection,
        ILogger<NoticeCenterViewModel> logger)
    {
        _monitor = monitor;
        _repoRepository = repoRepository;
        _gameRepository = gameRepository;
        _profileService = profileService;
        _bindingStore = bindingStore;
        _applyService = applyService;
        _navigation = navigation;
        _environment = environment;
        _dismissals = dismissals;
        _problems = problems;
        _updates = updates;
        _connection = connection;
        _logger = logger;

        _monitor.Changed += OnDriftChanged;
        _gameRepository.Games.CollectionChanged += OnGamesChanged;

        // Drift is detected from the manifest and the folder, so a notice can be up before the repo
        // list has been fetched - this is raised from the window's constructor, and the repos are
        // loaded by a command on the shell underneath it. Everything a notice says about membership
        // is unknowable until they land, and nothing else would re-ask.
        _repoRepository.Repos.CollectionChanged += OnReposChanged;

        // Everything else that can change the answer, wired here rather than remembered at each call
        // site. A user who edits a profile, repoints a game or checks a save out and then tabs back
        // to the game must not be the first to find out that they are out of sync - so the check is
        // driven by the facts changing, not by anybody remembering to ask.
        _gameRepository.GameChanged += OnFactsChanged;
        _profileService.ProfileUpdated += OnProfileUpdated;
        _bindingStore.BindingsChanged += OnFactsChanged;

        _dismissals.Changed += OnRedrawNeeded;
        _problems.Changed += OnRedrawNeeded;
        _updates.Changed += OnRedrawNeeded;
        _connection.Changed += OnRedrawNeeded;
    }


    /// <summary>What the column draws, in order, already capped by <see cref="MaxVisible"/>.</summary>
    public ObservableCollection<NoticeViewModel> Notices { get; } = [];

    [ObservableProperty]
    private bool _isVisible;

    /// <summary>"and 3 more" for the cards below the cap. Null while everything fits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverflow))]
    private string? _overflow;

    /// <summary>Whether the cap has been lifted by the user. Survives rebuilds; reset when it empties.</summary>
    [ObservableProperty]
    private bool _showAll;

    /// <summary>Whether there is more than one card, which is when waving them all away is worth offering.</summary>
    [ObservableProperty]
    private bool _canDismissAll;

    /// <summary>
    /// Whether the column is down to its rail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Temporary by construction.</b> There is no persisted form of it and it is not a dismissal:
    /// a notice arriving that the user has not seen re-opens the column, because the whole argument
    /// for the column is that drift has to be unmissable. Collapsing says "not while I am doing
    /// this", and dismissing says "I have read it" - and only one of those survives new news.
    /// </para>
    /// <para>
    /// The rail it collapses to is a real column of the window rather than an overlay, so content is
    /// never underneath it. The expanded panel is the overlay, because it opens uninvited and taking
    /// 420px from a mod list mid-scroll would recompute every column width under somebody's hands.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    private bool _isCollapsed;

    /// <summary>
    /// What the rail is coloured by: the worst thing waiting behind it.
    /// </summary>
    /// <remarks>
    /// A stripe and the counts rather than the whole rail, so that severity is a scale rather than a
    /// switch. A rail that turns solid red for a critical is also solid grey for everything else,
    /// which spends the loudest thing on screen on the difference between "look now" and "look".
    /// </remarks>
    [ObservableProperty]
    private NoticeSeverity _highestSeverity = NoticeSeverity.Info;

    /// <summary>
    /// How many of each severity, worst first, for the rail to say while it is closed.
    /// </summary>
    public ObservableCollection<NoticeSeverityCountViewModel> SeverityCounts { get; } = [];

    public bool HasOverflow => Overflow is not null;

    /// <summary>
    /// Raised at the end of every rebuild with everything that is live - after dismissals, before the
    /// cap, and including what the open editor suppresses from the column - on the UI thread. For the one listener that is not the column: Windows toasts, which say
    /// the same things to somebody the column cannot reach.
    /// </summary>
    public event EventHandler<IReadOnlyList<Notice>>? Refreshed;

    /// <summary>The notices as of the last rebuild, so a toast can be followed to the one it named.</summary>
    private IReadOnlyList<Notice> _live = [];

    /// <summary>
    /// Goes where a notice points - the mod list for a folder, the save for a savegame - without
    /// changing anything.
    /// </summary>
    /// <remarks>
    /// <b>Looking, never doing.</b> A toast is answered from another window, often after the moment has
    /// passed, so it only ever navigates: Re-apply is deliberately not reachable from here, and a notice
    /// with nothing to look at simply leaves the window open on the column.
    /// </remarks>
    /// <returns>False where the notice is gone, or has nowhere to go.</returns>
    public async Task<bool> OpenAsync(string key)
    {
        if (_live.FirstOrDefault(x => x.Key == key) is not Notice notice)
        {
            return false;
        }

        var target = notice.Actions.FirstOrDefault(x => x.Kind is NoticeActionKind.Review or NoticeActionKind.OpenSavegame);

        if (target is null)
        {
            return false;
        }

        await InvokeAsync(notice, target.Kind);

        return true;
    }


    /// <summary>The first check plus the watcher, once the shell is up.</summary>
    public void Start()
    {
        _ = _monitor.CheckAsync();

        _monitor.Watch();
    }

    /// <summary>
    /// Window activation. Throttled inside the monitor, since this fires on every alt-tab and someone
    /// switching back and forth does not need a directory listing each time.
    /// </summary>
    public void NotifyWindowActivated()
    {
        _ = _monitor.CheckAsync(DriftCheckReason.WindowActivated);
    }

    /// <summary>
    /// Hides a profile's notices while the user is looking at the very thing they would tell them
    /// about. Paired with <see cref="Release"/> when that editor closes - never persisted, and never
    /// widened to a second surface.
    /// </summary>
    public void SuppressFor(ActiveProfile profile)
    {
        _suppressed.Add(profile);

        Refresh();
    }

    /// <summary>
    /// Stops suppressing, and re-checks rather than only redrawing.
    /// </summary>
    /// <remarks>
    /// The editor is the one page that can change what these notices would say while it is being told
    /// not to say them - removing a mod and saving without applying is exactly that - so the last
    /// computed answer is the one thing that must not be trusted at the moment the suppression lifts.
    /// </remarks>
    public void Release(ActiveProfile profile)
    {
        _suppressed.Remove(profile);

        Refresh();

        _ = _monitor.CheckAsync();
    }

    public void Dispose()
    {
        _monitor.Changed -= OnDriftChanged;
        _gameRepository.Games.CollectionChanged -= OnGamesChanged;
        _repoRepository.Repos.CollectionChanged -= OnReposChanged;
        _gameRepository.GameChanged -= OnFactsChanged;
        _profileService.ProfileUpdated -= OnProfileUpdated;
        _bindingStore.BindingsChanged -= OnFactsChanged;
        _dismissals.Changed -= OnRedrawNeeded;
        _problems.Changed -= OnRedrawNeeded;
        _updates.Changed -= OnRedrawNeeded;
        _connection.Changed -= OnRedrawNeeded;
    }


    [RelayCommand]
    private void DismissAll()
    {
        // The drift half goes to the ledger and the absorbed half starts its cooldown. Two
        // mechanisms because they mean different things - see BackgroundProblemSource - and one
        // button, because the user is saying the same thing to both.
        _dismissals.DismissAll([.. Notices
            .Select(x => x.Model)
            .Where(x => BackgroundProblemSource.Owns(x.Key) is false)]);

        if (Notices.Any(x => BackgroundProblemSource.Owns(x.Key)))
        {
            _problems.Dismiss();
        }
    }

    [RelayCommand]
    private void ToggleShowAll()
    {
        ShowAll = ShowAll is false;

        Refresh();
    }

    /// <summary>
    /// Down to the rail, or back up. Collapsing also drops the show-all, so re-opening starts at the
    /// cap again rather than at whatever the last look left behind.
    /// </summary>
    [RelayCommand]
    private void ToggleCollapsed()
    {
        IsCollapsed = IsCollapsed is false;

        if (IsCollapsed)
        {
            ShowAll = false;
        }

        Refresh();
    }


    private void Dismiss(Notice notice)
    {
        if (BackgroundProblemSource.Owns(notice.Key))
        {
            _problems.Dismiss();

            return;
        }

        _dismissals.Dismiss(notice);
    }

    /// <summary>
    /// Runs one card's action.
    /// </summary>
    /// <remarks>
    /// Every outcome that is worth a word goes back onto the card that was pressed rather than into a
    /// dialog: these are all things the user can carry on ignoring, and a modal is exactly what this
    /// column exists not to be.
    /// </remarks>
    private async Task InvokeAsync(Notice notice, NoticeActionKind kind)
    {
        var card = _cards.GetValueOrDefault(notice.Key);

        switch (kind)
        {
            case NoticeActionKind.OpenLog:
                if (LogFolder.TryOpen(_logger) is false)
                {
                    card?.ReportStatus($"The log folder could not be opened. It is in {LogFolder.Path}.");
                }

                break;

            case NoticeActionKind.Review:
                await ReviewAsync(notice, card);

                break;

            case NoticeActionKind.OpenSavegame:
                await OpenSavegameAsync(notice, card);

                break;

            case NoticeActionKind.Reapply:
                await ReapplyAsync(notice, card);

                break;

            case NoticeActionKind.RestartToUpdate:
                await _updates.RestartAsync();

                break;

            case NoticeActionKind.RetryConnection:
                _connection.RetryNow();

                break;
        }
    }

    private async Task ReviewAsync(Notice notice, NoticeViewModel? card)
    {
        if (notice.Subject is not NoticeSubject subject
            || subject.RepoId is not Guid repoId
            || subject.ProfileId is not Guid profileId
            || subject.Target is not { } target)
        {
            card?.ReportStatus("There is no mod folder on this one to look at.");

            return;
        }

        if (await _navigation.GoToProfileModsAsync(repoId, profileId, target) is false)
        {
            card?.ReportStatus("That profile could not be opened from here - pick it in the sidebar.");
        }
    }

    private async Task OpenSavegameAsync(Notice notice, NoticeViewModel? card)
    {
        if (notice.Subject is not NoticeSubject subject
            || subject.RepoId is not Guid repoId
            || subject.SavegameId is not Guid savegameId)
        {
            return;
        }

        if (await _navigation.GoToSavegamesAsync(repoId, savegameId) is false)
        {
            card?.ReportStatus("That save could not be opened from here - pick its repo in the sidebar.");
        }
    }

    /// <summary>
    /// The game this notice is about, not every game on the profile: the notice names one, and a game
    /// is configured once, so there is nothing else the profile could reach. It does reach every
    /// folder that game has, which is the apply's own loop.
    /// </summary>
    private async Task ReapplyAsync(Notice notice, NoticeViewModel? card)
    {
        if (notice.Subject is not NoticeSubject subject
            || subject.RepoId is not Guid repoId
            || subject.ProfileId is not Guid profileId)
        {
            return;
        }

        if (_repoRepository.Repos.FirstOrDefault(x => x.Id == repoId) is not Repo repo)
        {
            // The same window a Review button is missing in: a notice can be up before the repo list
            // has arrived. Saying so beats a button that does nothing when pressed.
            card?.ReportStatus("The repo this game follows has not loaded yet. Try again in a moment.");

            return;
        }

        if (_gameRepository.Find(subject.Game) is not Game game)
        {
            card?.ReportStatus("This game is no longer connected on this machine.");

            return;
        }

        card?.ReportStatus("Re-applying...");

        // Pure apply - the game already follows this profile, which is why it is drifted from it.
        var result = await _applyService.ApplyAsync(
            repo,
            game,
            profileId,
            subject.ProfileName,
            confirmPlan: false,
            progress: null,
            CancellationToken.None);

        card?.ReportStatus(result.Message);

        await _monitor.CheckAsync();
    }


    private void OnDriftChanged(object? sender, EventArgs e) => Post(Refresh);

    private void OnRedrawNeeded(object? sender, EventArgs e) => Post(Refresh);

    /// <summary>
    /// The repo list arriving, or being swapped for another account's. No drift check is needed - the
    /// drift has not changed, only what is known about who the user is in the repo it belongs to.
    /// </summary>
    private void OnReposChanged(object? sender, NotifyCollectionChangedEventArgs e) => Post(Refresh);

    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A folder that just arrived is not being watched yet, and one that left is being watched for
        // nothing.
        _monitor.Watch();

        _ = _monitor.CheckAsync();
    }

    /// <summary>
    /// Something the check reads has changed: a game repointed, a profile that moved on, a savegame
    /// taken or handed back.
    /// </summary>
    /// <remarks>
    /// <see cref="DriftCheckReason.Explicit"/> by default, so the throttle never swallows one of
    /// these - they are consequences of something the user just did, and the whole complaint these
    /// answer is a notice that arrives one alt-tab too late.
    /// </remarks>
    private void OnFactsChanged(object? sender, EventArgs e)
    {
        // Re-watched as well as re-checked: a game whose mod folder moved is being watched at the
        // old path.
        _monitor.Watch();

        _ = _monitor.CheckAsync();
    }

    private void OnProfileUpdated(Guid profileId)
    {
        // A profile's head revision moving is drift for every folder built against the old one,
        // whether this client saved it or a teammate did and a refresh brought it back.
        _ = _monitor.CheckAsync();
    }

    /// <summary>The monitor runs its checks off the UI thread, and everything below is bound.</summary>
    private static void Post(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);


    private void Refresh()
    {
        // First, because while it is up everything below it is working from a repo list that has not
        // arrived.
        var built = _connection.Build()
            .Concat(NoticeBuilder.Build(_monitor.Drifted, _monitor.StoreCorruption, _environment))
            .Concat(_problems.Build())
            .Concat(_updates.ReadyVersion is string ready ? [UpdateNotice.For(ready)] : [])
            .ToList();

        // What the open editor already shows, built on its own to learn the keys: notices are built per
        // game, and it is whole games that are suppressed.
        var suppressed = NoticeBuilder.Build(
                [.. _monitor.Drifted.Where(x => x.Game.ActiveProfile is ActiveProfile active && _suppressed.Contains(active))],
                [],
                _environment)
            .Select(x => x.Key)
            .ToHashSet();

        // Forgotten before they are applied, so a problem waved away and then actually fixed leaves
        // nothing behind to silence the same problem next week.
        _dismissals.Retain(built.Select(x => x.Key));

        var undismissed = built
            .Where(x => x.CanDismiss is false || _dismissals.IsDismissed(x.Key, x.Signature) is false)
            .ToList();

        var live = undismissed
            .Where(x => suppressed.Contains(x.Key) is false)
            .ToList();

        // News re-opens the column, and a rebuild of what is already on screen does not. Measured
        // against everything seen this session rather than against what is drawn: a notice the user
        // dismissed and which came back under the same signature is not news to them.
        if (live.Any(x => _seen.Contains(x.Key) is false))
        {
            IsCollapsed = false;
        }

        foreach (var notice in live)
        {
            _seen.Add(notice.Key);
        }

        _live = undismissed;

        CanDismissAll = live.Count(x => x.CanDismiss) > 1;

        // Off the whole live set, never off the capped one: a rail reporting "1 critical" because the
        // other two fell below the fold would be the summarising-in-prose problem all over again, in
        // the one place the user is trusting to tell them whether to look.
        HighestSeverity = live.Count > 0 ? live.Min(x => x.Severity) : NoticeSeverity.Info;

        SyncCounts(live);

        var shown = ShowAll || live.Count <= MaxVisible
            ? live
            : live.Take(MaxVisible).ToList();

        Overflow = live.Count > shown.Count
            ? $"{live.Count - shown.Count} more"
            : ShowAll && live.Count > MaxVisible ? "Show fewer" : null;

        Reconcile(shown);

        IsVisible = Notices.Count > 0;

        if (IsVisible is false)
        {
            ShowAll = false;

            // Nothing to come back to, so the next notice that does arrive opens rather than landing
            // behind a rail somebody closed over an unrelated problem an hour ago.
            IsCollapsed = false;
        }

        // Suppression included: it stands for somebody looking at the editor, and a window in the tray
        // still has the editor as its page with nobody looking at it. Whether they are looking is the
        // toasts' own question, and one they already ask.
        Refreshed?.Invoke(this, undismissed);
    }

    /// <summary>
    /// Rebuilds the rail's per-severity counts, worst first.
    /// </summary>
    /// <remarks>
    /// In place and only where they differ, because this runs on every drift check - which is every
    /// alt-tab - and replacing the collection makes the rail's text flicker on a window that came
    /// forward and found nothing changed.
    /// </remarks>
    private void SyncCounts(IReadOnlyList<Notice> live)
    {
        var counts = live
            .GroupBy(x => x.Severity)
            .OrderBy(x => x.Key)
            .Select(x => new NoticeSeverityCountViewModel(x.Key, x.Count()))
            .ToList();

        if (SeverityCounts.Select(x => x.Label).SequenceEqual(counts.Select(x => x.Label)))
        {
            return;
        }

        SeverityCounts.Clear();

        foreach (var count in counts)
        {
            SeverityCounts.Add(count);
        }
    }

    /// <summary>
    /// Brings the bound collection into line with a freshly built list, in place.
    /// </summary>
    /// <remarks>
    /// By key rather than by clearing and refilling, because a rebuild happens on every drift check -
    /// which is every alt-tab - and a card replaced wholesale loses the expansion the user just
    /// clicked, the status of the re-apply it is running, and the focus if they were tabbing through
    /// its buttons.
    /// </remarks>
    private void Reconcile(IReadOnlyList<Notice> notices)
    {
        foreach (var stale in _cards.Keys.Where(x => notices.Any(n => n.Key == x) is false).ToList())
        {
            _cards.Remove(stale);
        }

        for (var index = 0; index < notices.Count; index++)
        {
            var notice = notices[index];

            if (_cards.TryGetValue(notice.Key, out var card))
            {
                card.Update(notice);
            }
            else
            {
                // Open where it is one of the first two or where something is at stake; collapsed
                // otherwise. A column of eight open cards after one alt-tab is the wall.
                card = new NoticeViewModel(
                    notice,
                    expanded: index < 2 || notice.Severity is NoticeSeverity.Critical,
                    InvokeAsync,
                    Dismiss);

                _cards[notice.Key] = card;
            }

            // Only the first card of a run draws the game's name: the centre is the only thing that
            // can see the card above this one.
            card.StartsGroup = index == 0 || notices[index - 1].GroupLabel != notice.GroupLabel;

            if (index < Notices.Count)
            {
                if (ReferenceEquals(Notices[index], card) is false)
                {
                    Notices[index] = card;
                }
            }
            else
            {
                Notices.Add(card);
            }
        }

        while (Notices.Count > notices.Count)
        {
            Notices.RemoveAt(Notices.Count - 1);
        }
    }
}
