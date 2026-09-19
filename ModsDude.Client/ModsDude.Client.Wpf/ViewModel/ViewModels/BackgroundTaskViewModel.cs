using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Transfers;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The strip along the top of the window that says something is still running.
/// </summary>
/// <remarks>
/// <para>
/// <b>Along the top edge rather than in the corner with the notices.</b> The two things stacked
/// bottom-right are both about something that has already happened and both offer a way to deal with
/// it; this one is about something happening now and offers nothing, so putting it in that stack
/// would mean the corner sometimes reported the past and sometimes the present.
/// </para>
/// <para>
/// <b>One task at a time, paged through rather than stacked.</b> Several bars racing each other is
/// less legible than one, and a strip that grew a card per task would resize the top of the window
/// every time something started. So the others are reachable by the arrows and nothing moves on its
/// own - see <see cref="Begin"/> for why a new task does not take the strip.
/// </para>
/// <para>
/// <b>Inside a task, only the slow parts are drawn.</b> Five parallel uploads writing their names
/// into one line is the line flickering; five rows churning under it is the same thing with the
/// strip's height added. A part earns a row by outliving <see cref="PromoteAfter"/>, everything
/// faster is counted, and a row once earned is never taken back.
/// </para>
/// <para>
/// <b>Not dismissible, and not a modal.</b> There is nothing to acknowledge - it goes away when the
/// work does. Which is also why every handle is disposable: a <c>using</c> at the call site is what
/// guarantees the strip disappears on the cancellation and failure paths too.
/// </para>
/// <para>
/// <b>Cancel is the one thing it does offer</b>, and only for the task on screen. It has to live here
/// rather than on the page that started the job, because the page is rebuilt on every navigation and
/// the whole point of the strip is that the user is free to navigate.
/// See <see cref="IBackgroundTaskReporter.Begin"/>.
/// </para>
/// </remarks>
public partial class BackgroundTaskViewModel : ObservableObject, IBackgroundTaskReporter
{
    /// <summary>How long a part has to run before it earns a row and a bar of its own.</summary>
    private static readonly TimeSpan PromoteAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long a row stays after its part has finished, so one that ends just past the threshold
    /// cannot flash a bar up and take it away again.
    /// </summary>
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Dead time on Cancel after a task ends and its neighbour takes the strip, so a click aimed at
    /// work that has just finished cannot land on work that has not.
    /// </summary>
    private static readonly TimeSpan CancelGuard = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often the strip redraws itself. Also the whole of the throttle: reports arrive from byte
    /// callbacks far faster than a 3px bar can say anything, and each one used to cross to the UI
    /// thread on its own.
    /// </summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    /// <summary>Rows drawn at once. The rest are a count, for the same reason the fast ones are.</summary>
    private const int MaxRows = 3;


    private readonly Lock _lock = new();
    private readonly List<RunningTask> _running = [];
    private readonly HashSet<RunningTask> _seen = [];
    private readonly DispatcherTimer? _timer;
    private readonly TransferLimits _transferLimits;

    private RunningTask? _shown;
    private long _cancelGuardFrom;


    public BackgroundTaskViewModel(TransferLimits transferLimits)
    {
        _transferLimits = transferLimits;

        // A limit changed in settings while a download runs changes what its title should say, and
        // nothing else would redraw it until the next report.
        transferLimits.Changed += (_, _) => Publish(immediate: true);

        // Null in a designer and in a test host, where there is nothing to draw on anyway. Without a
        // timer the strip still reports; it just never promotes a part to a row of its own.
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Tick };
        _timer.Tick += (_, _) => Apply();
    }


    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyPropertyChangedFor(nameof(HasStatusLine))]
    private string? _detail;

    /// <summary>
    /// "Downloads capped at 50 Mbit/s", for a task moving bytes a user limit is holding back. A line
    /// of its own, because on the title it pushed the name of the work itself off the end.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLimit))]
    private string? _limit;

    /// <summary>0 to 100, and meaningless while <see cref="IsIndeterminate"/> is true.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>
    /// "about 3 min left", for the task on screen once it has been watched long enough to guess. On
    /// the task and not on its parts: a row's bytes are a fraction of a job whose other rows have not
    /// started, so the only figure that means anything is for the whole.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemaining))]
    [NotifyPropertyChangedFor(nameof(HasStatusLine))]
    private string? _remaining;

    /// <summary>"and 2 more taking a while", for the rows past <see cref="MaxRows"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreSubtasks))]
    private string? _moreSubtasks;

    /// <summary>"2 of 3", and null while there is only one task.</summary>
    [ObservableProperty]
    private string? _pagerText;

    [ObservableProperty]
    private bool _hasMultiple;

    [ObservableProperty]
    private bool _canGoPrevious;

    [ObservableProperty]
    private bool _canGoNext;

    /// <summary>Whether some other task has not been looked at, which puts a dot beside the pager.</summary>
    [ObservableProperty]
    private bool _hasUnseen;

    /// <summary>
    /// Whether there is a row to draw at all. Bound rather than left to an empty list, so a task with
    /// nothing slow in it does not carry the rows' margin around as dead space.
    /// </summary>
    [ObservableProperty]
    private bool _hasSubtasks;

    /// <summary>Whether the shown task can be stopped at all, which is what draws the button.</summary>
    [ObservableProperty]
    private bool _canCancel;

    /// <summary>
    /// Whether it can be pressed. Separate from <see cref="CanCancel"/> so the guard greys the button
    /// rather than removing it, which would move everything beside it for a second.
    /// </summary>
    [ObservableProperty]
    private bool _isCancelEnabled;

    /// <summary>The slow parts of the shown task, longest-promoted first.</summary>
    public ObservableCollection<BackgroundSubtaskViewModel> Subtasks { get; } = [];

    public bool HasDetail => string.IsNullOrWhiteSpace(Detail) is false;
    public bool HasRemaining => Remaining is not null;

    /// <summary>Whether the line under the title has anything on it, either side.</summary>
    public bool HasStatusLine => HasDetail || HasRemaining;
    public bool HasLimit => Limit is not null;
    public bool HasMoreSubtasks => MoreSubtasks is not null;


    /// <summary>
    /// Stops the shown task, once.
    /// </summary>
    /// <remarks>
    /// <b>Asks rather than ends.</b> Cancelling a sync means "stop at the next file", not "stop now" -
    /// the work between two files is what keeps a mod folder describable - so this signals and the
    /// strip stays up, with the entry disappearing when the work actually unwinds. A second press does
    /// nothing, which is why the button goes away with the first.
    /// </remarks>
    [RelayCommand]
    private void Cancel()
    {
        Action? cancel;

        lock (_lock)
        {
            cancel = InCancelGuard() ? null : _shown?.TakeCancel();
        }

        if (cancel is null)
        {
            return;
        }

        // Outside the lock: cancellation runs registered callbacks synchronously, and those belong to
        // whatever is doing the work rather than to the strip that is drawing it.
        cancel.Invoke();

        Publish(immediate: true);
    }


    [RelayCommand]
    private void Previous() => Page(-1);

    [RelayCommand]
    private void Next() => Page(1);

    private void Page(int delta)
    {
        lock (_lock)
        {
            if (_shown is null)
            {
                return;
            }

            var next = _running.IndexOf(_shown) + delta;

            if (next < 0 || next >= _running.Count)
            {
                return;
            }

            _shown = _running[next];
        }

        Publish(immediate: true);
    }


    /// <summary>
    /// Announces a task, and shows it only if the strip was empty.
    /// </summary>
    /// <remarks>
    /// <b>It does not take the strip from whatever is already on it.</b> Following the newest is right
    /// exactly once - when there was nothing to look at - and wrong every time after, because it pulls
    /// a five minute import off screen for a check that runs for 300ms, and swaps the Cancel button
    /// under the pointer while it does. The arrival shows as a dot beside the pager instead.
    /// </remarks>
    public IBackgroundTask Begin(string title, string? detail = null, Action? cancel = null)
    {
        var task = new RunningTask(this, title, detail, cancel);

        lock (_lock)
        {
            var wasIdle = _running.Count == 0;

            _running.Add(task);

            if (wasIdle)
            {
                _shown = task;
            }
        }

        Publish(immediate: true);

        return task;
    }


    private void End(RunningTask task)
    {
        lock (_lock)
        {
            var index = _running.IndexOf(task);

            // Idempotent: a handle disposed twice - a using inside a using, a finally after an early
            // return - must not take the count negative and hide a task that is still running.
            if (index < 0)
            {
                return;
            }

            _running.RemoveAt(index);
            _seen.Remove(task);

            if (ReferenceEquals(_shown, task) is false)
            {
                return;
            }

            // The older neighbour first - the pager reads left to right in start order, so falling
            // backwards lands where the user was already looking. Whatever takes over does so without
            // being asked, which is why its Cancel is dead for a moment.
            _shown = _running.ElementAtOrDefault(index - 1) ?? _running.ElementAtOrDefault(index);

            if (_shown is not null)
            {
                _cancelGuardFrom = Stopwatch.GetTimestamp();
            }
        }

        Publish(immediate: true);
    }


    private bool InCancelGuard() => _cancelGuardFrom > 0 && Stopwatch.GetElapsedTime(_cancelGuardFrom) < CancelGuard;


    /// <summary>
    /// Asks for a redraw.
    /// </summary>
    /// <remarks>
    /// Ordinary reports are coalesced onto the timer, which is what keeps five parallel uploads from
    /// dispatching a redraw per buffer. Structural changes - a task starting, ending, being paged to -
    /// go straight across, because waiting a tenth of a second to acknowledge a click reads as a miss.
    /// </remarks>
    private void Publish(bool immediate = false)
    {
        if (immediate is false && _timer is not null)
        {
            return;
        }

        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Apply();

            return;
        }

        // InvokeAsync rather than Invoke, because a report must never make the thing being reported
        // on wait for a redraw.
        _ = dispatcher.InvokeAsync(Apply);
    }

    /// <summary>
    /// Reads the current state and pushes it at the bound properties, on the UI thread.
    /// </summary>
    private void Apply()
    {
        Snapshot snapshot;

        // Copied out under the same lock the reports are written under, so the strip can never draw
        // one task's title beside another's count. Nothing is bound inside the lock: a property
        // change raises handlers synchronously, and none of them has any business running under it.
        lock (_lock)
        {
            snapshot = Read();
        }

        if (snapshot.Count == 0)
        {
            _timer?.Stop();

            IsVisible = false;
            Subtasks.Clear();
            HasSubtasks = false;
            MoreSubtasks = null;
            PagerText = null;
            HasMultiple = false;
            HasUnseen = false;
            CanCancel = false;
            IsCancelEnabled = false;

            return;
        }

        _timer?.Start();

        Title = snapshot.Title;
        Detail = Compose(snapshot);
        Limit = snapshot.Limit;
        Remaining = snapshot.Remaining is { } left ? RemainingTimeEstimator.Describe(left) : null;
        IsIndeterminate = snapshot.Total <= 0;
        Progress = snapshot.Total > 0 ? Math.Clamp(snapshot.Completed * 100d / snapshot.Total, 0, 100) : 0;

        CanCancel = snapshot.CanCancel;
        IsCancelEnabled = snapshot.CanCancel && snapshot.InCancelGuard is false;

        HasMultiple = snapshot.Count > 1;
        PagerText = snapshot.Count > 1 ? $"{snapshot.Index + 1} of {snapshot.Count}" : null;
        CanGoPrevious = snapshot.Index > 0;
        CanGoNext = snapshot.Index < snapshot.Count - 1;
        HasUnseen = snapshot.HasUnseen;

        Sync(snapshot.Rows);
        HasSubtasks = Subtasks.Count > 0;

        MoreSubtasks = snapshot.Hidden switch
        {
            0 => null,
            1 => "and 1 more taking a while",
            var n => $"and {n} more taking a while"
        };

        IsVisible = true;
    }

    /// <summary>The whole of what the strip draws, read in one go. Under the lock.</summary>
    private Snapshot Read()
    {
        // Every task, not only the one on screen: sweeping is what retires a finished row once its
        // dwell is up, and a two thousand mod import the user has paged away from would otherwise
        // keep every one of them.
        foreach (var task in _running)
        {
            task.Sweep();
        }

        // Defensive. Nothing should empty this while work is running, and a strip that hid itself
        // over it would be the one failure nobody could diagnose from the screen.
        _shown ??= _running.FirstOrDefault();

        if (_shown is not { } shown)
        {
            return default;
        }

        _seen.Add(shown);

        var rows = shown.Rows(MaxRows, out var hidden);

        return new Snapshot
        {
            Count = _running.Count,
            Index = _running.IndexOf(shown),
            Title = shown.Title,

            // Read at every redraw rather than fixed when the task began, so a limit set or lifted
            // part way through is what the strip says from then on.
            Limit = TransferRate.DescribeLimits(_transferLimits, shown.Transfers),
            Detail = shown.Detail,
            Completed = shown.Completed,
            Total = shown.Total,
            Amount = shown.Amount,
            Remaining = shown.Remaining,
            Running = shown.LiveSubtasks,
            Rows = rows,
            Hidden = hidden,
            CanCancel = shown.CanCancel,
            InCancelGuard = InCancelGuard(),
            HasUnseen = _running.Exists(x => _seen.Contains(x) is false)
        };
    }

    /// <summary>
    /// The second line: what the task said, how far through it is, and how many parts are moving.
    /// </summary>
    /// <remarks>
    /// <b>The count is what replaced the name that used to flicker here.</b> Nobody can read five mod
    /// names cycling at ten a second, and the thing they were carrying between them - that five are
    /// moving - is what a number says without moving at all.
    /// </remarks>
    private static string? Compose(Snapshot snapshot)
    {
        var parts = new List<string>(3);

        if (string.IsNullOrWhiteSpace(snapshot.Detail) is false)
        {
            parts.Add(snapshot.Detail);
        }

        if (snapshot.Amount is not null)
        {
            parts.Add(snapshot.Amount);
        }
        else if (snapshot.Total > 0)
        {
            parts.Add($"{snapshot.Completed} of {snapshot.Total}");
        }

        // Only where there is something to disambiguate. A sync runs one item at a time, and "1
        // running" beside its own count says nothing the count did not.
        if (snapshot.Running > 1)
        {
            parts.Add($"{snapshot.Running} running");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    /// <summary>
    /// Brings the bound rows to match, in place.
    /// </summary>
    /// <remarks>
    /// Matched by key rather than rebuilt: replacing the collection ten times a second would restart
    /// every bar in it, which is the one thing a progress bar must not do.
    /// </remarks>
    private void Sync(IReadOnlyList<Row> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (i >= Subtasks.Count || ReferenceEquals(Subtasks[i].Key, row.Key) is false)
            {
                var at = IndexOf(row.Key);

                if (at >= 0)
                {
                    Subtasks.Move(at, i);
                }
                else
                {
                    Subtasks.Insert(i, new BackgroundSubtaskViewModel(row.Key));
                }
            }

            var bound = Subtasks[i];

            bound.Name = row.Name;
            bound.Amount = row.Amount;
            bound.IsIndeterminate = row.Total <= 0;
            bound.Progress = row.Total > 0 ? Math.Clamp(row.Completed * 100d / row.Total, 0, 100) : 0;
        }

        while (Subtasks.Count > rows.Count)
        {
            Subtasks.RemoveAt(Subtasks.Count - 1);
        }
    }

    private int IndexOf(object key)
    {
        for (var i = 0; i < Subtasks.Count; i++)
        {
            if (ReferenceEquals(Subtasks[i].Key, key))
            {
                return i;
            }
        }

        return -1;
    }


    private readonly record struct Row(object Key, string Name, long Completed, long Total, string? Amount);

    private readonly record struct Snapshot
    {
        public int Count { get; init; }
        public int Index { get; init; }
        public string Title { get; init; }
        public string? Limit { get; init; }
        public string? Detail { get; init; }
        public long Completed { get; init; }
        public long Total { get; init; }
        public string? Amount { get; init; }
        public TimeSpan? Remaining { get; init; }
        public int Running { get; init; }
        public IReadOnlyList<Row> Rows { get; init; }
        public int Hidden { get; init; }
        public bool CanCancel { get; init; }
        public bool InCancelGuard { get; init; }
        public bool HasUnseen { get; init; }
    }


    /// <summary>
    /// One announced piece of work. Its fields are written from whichever thread is doing the work and
    /// read under the same lock the list is, so the strip never renders half of an update.
    /// </summary>
    private sealed class RunningTask(BackgroundTaskViewModel owner, string title, string? detail, Action? cancel)
        : IBackgroundTask
    {
        private readonly List<RunningSubtask> _subtasks = [];
        private readonly RemainingTimeEstimator _remaining = new();

        private Action? _cancel = cancel;


        public string Title { get; private set; } = title;
        public string? Detail { get; private set; } = detail;
        public long Completed { get; private set; }
        public long Total { get; private set; }
        public string? Amount { get; private set; }
        public TransferDirection Transfers { get; private set; }

        public TimeSpan? Remaining => _remaining.Remaining;

        public bool CanCancel => _cancel is not null;

        public int LiveSubtasks => _subtasks.Count(x => x.IsLive);


        /// <summary>
        /// Hands over the cancellation, leaving none behind.
        /// </summary>
        /// <remarks>
        /// Taken rather than read, so the button can only fire once: a sync unwinds between files and
        /// the entry stays on the strip until it does, which is exactly long enough for somebody to
        /// press Cancel three more times and wonder why nothing is happening.
        /// </remarks>
        public Action? TakeCancel()
        {
            var cancel = _cancel;

            _cancel = null;

            return cancel;
        }


        public void Report(string? detail) => Report(detail, 0, 0);

        public void Report(string? detail, long completed, long total, string? amount = null)
        {
            lock (owner._lock)
            {
                Detail = detail;
                Completed = completed;
                Total = total;
                Amount = amount;

                // The detail is the stage: a sync's phase and a savegame's step are each their own
                // distance, so a new one is a new estimate rather than a bend in the old one.
                _remaining.Observe(detail, completed, total);
            }

            owner.Publish();
        }

        public void Retitle(string title)
        {
            lock (owner._lock)
            {
                Title = title;
            }

            owner.Publish();
        }

        public void DeclareTransfers(TransferDirection directions)
        {
            lock (owner._lock)
            {
                Transfers |= directions;
            }

            owner.Publish(immediate: true);
        }

        public IBackgroundSubtask BeginSubtask(string name, bool longRunning = false)
        {
            var subtask = new RunningSubtask(owner, this, name, longRunning);

            lock (owner._lock)
            {
                _subtasks.Add(subtask);
            }

            owner.Publish();

            return subtask;
        }

        public void EndSubtask(RunningSubtask subtask)
        {
            lock (owner._lock)
            {
                // A part that never earned a row just goes. One that did is kept for its dwell, so a
                // bar cannot appear and vanish inside the same breath.
                if (subtask.IsPromoted)
                {
                    subtask.Finish();
                }
                else
                {
                    _subtasks.Remove(subtask);
                }
            }

            owner.Publish();
        }

        /// <summary>Promotes what is due and drops what has outstayed its dwell. Under the lock.</summary>
        public void Sweep()
        {
            foreach (var subtask in _subtasks)
            {
                subtask.PromoteIfDue();
            }

            _subtasks.RemoveAll(x => x.IsExpired);
        }

        /// <summary>
        /// The rows to draw, longest-promoted first.
        /// </summary>
        /// <remarks>
        /// Ordered by when they were promoted rather than by progress or by name: a list that re-sorts
        /// as bars fill is unreadable, and promotion is the only event that may move a row.
        /// </remarks>
        public IReadOnlyList<Row> Rows(int max, out int hidden)
        {
            var promoted = _subtasks.Where(x => x.IsPromoted).OrderBy(x => x.PromotedAt).ToList();

            hidden = Math.Max(0, promoted.Count - max);

            while (promoted.Count > max)
            {
                // Over the cap a finished row gives up its place to a running one before the newest
                // does: it is already saying less than the others.
                var drop = promoted.FindLastIndex(x => x.IsLive is false);

                promoted.RemoveAt(drop >= 0 ? drop : promoted.Count - 1);
            }

            return [.. promoted.Select(x => new Row(x, x.Name, x.Completed, x.Total, x.Amount))];
        }

        public void Dispose() => owner.End(this);
    }


    /// <summary>One part of a task, drawn only once it has been going long enough to be worth a row.</summary>
    private sealed class RunningSubtask : IBackgroundSubtask
    {
        private readonly BackgroundTaskViewModel _owner;
        private readonly RunningTask _task;
        private readonly long _startedAt = Stopwatch.GetTimestamp();

        private long? _promotedAt;
        private long? _endedAt;


        public RunningSubtask(BackgroundTaskViewModel owner, RunningTask task, string name, bool longRunning)
        {
            _owner = owner;
            _task = task;

            Name = name;

            if (longRunning)
            {
                _promotedAt = _startedAt;
            }
        }


        public string Name { get; private set; }
        public long Completed { get; private set; }
        public long Total { get; private set; }
        public string? Amount { get; private set; }

        public bool IsLive => _endedAt is null;

        /// <summary>
        /// Whether it has earned a row - and it never gives one back. A part demoted for going quiet
        /// would take its bar away and put it back as the bytes stuttered, which is worse than a row
        /// that stays.
        /// </summary>
        public bool IsPromoted => _promotedAt is not null;

        public long PromotedAt => _promotedAt ?? long.MaxValue;

        public bool IsExpired => _endedAt is long ended && Stopwatch.GetElapsedTime(ended) >= Dwell;


        /// <summary>Under the owner's lock, from the sweep.</summary>
        public void PromoteIfDue()
        {
            if (IsPromoted is false && IsLive && Stopwatch.GetElapsedTime(_startedAt) >= PromoteAfter)
            {
                _promotedAt = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>Under the owner's lock.</summary>
        public void Finish() => _endedAt = Stopwatch.GetTimestamp();


        public void Report(long completed, long total, string? amount = null)
        {
            lock (_owner._lock)
            {
                Completed = completed;
                Total = total;
                Amount = amount;
            }

            _owner.Publish();
        }

        public void Rename(string name)
        {
            lock (_owner._lock)
            {
                Name = name;
            }

            _owner.Publish();
        }

        public void Dispose() => _task.EndSubtask(this);
    }
}
