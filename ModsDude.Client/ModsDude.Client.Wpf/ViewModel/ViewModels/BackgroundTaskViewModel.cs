using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Windows;

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
/// <b>One strip however many tasks there are.</b> It shows the most recently started one and counts
/// the rest, because two bars racing each other is less legible than one bar and a number - and
/// because a second concurrent task is rare enough that it never needs its own line. The most recent
/// rather than the oldest: what somebody just clicked is what they are waiting on.
/// </para>
/// <para>
/// <b>Not dismissible, and not a modal.</b> There is nothing to acknowledge - it goes away when the
/// work does. Which is also why every handle is disposable: a <c>using</c> at the call site is what
/// guarantees the strip disappears on the cancellation and failure paths too.
/// </para>
/// <para>
/// <b>Cancel is the one thing it does offer</b>, and only for work that gave it something to call.
/// Not a contradiction of the above: dismissing would hide a job that is still running, whereas this
/// stops the job - and it has to live here rather than on the page that started it, because the page
/// is rebuilt on every navigation and the whole point of the strip is that the user is free to
/// navigate. See <see cref="IBackgroundTaskReporter.Begin"/>.
/// </para>
/// </remarks>
public partial class BackgroundTaskViewModel : ObservableObject, IBackgroundTaskReporter
{
    private readonly Lock _lock = new();
    private readonly List<RunningTask> _running = [];


    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string? _detail;

    /// <summary>0 to 100, and meaningless while <see cref="IsIndeterminate"/> is true.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>"and 2 more" for the tasks this strip is not naming. Null while there is only one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOthers))]
    private string? _others;

    /// <summary>
    /// Whether the named task can be stopped, which is what draws the Cancel button.
    /// </summary>
    /// <remarks>
    /// Of the <em>named</em> task only - the one the strip is describing. Offering a Cancel while a
    /// count of others sits beside it would be a button whose target the user cannot see, and the
    /// named one is the most recently started, which is what somebody has just been waiting on.
    /// </remarks>
    [ObservableProperty]
    private bool _canCancel;

    public bool HasDetail => string.IsNullOrWhiteSpace(Detail) is false;
    public bool HasOthers => Others is not null;


    /// <summary>
    /// Stops the named task, once.
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
            cancel = _running.Count > 0 ? _running[^1].TakeCancel() : null;
        }

        if (cancel is null)
        {
            return;
        }

        // Outside the lock: cancellation runs registered callbacks synchronously, and those belong to
        // whatever is doing the work rather than to the strip that is drawing it.
        cancel.Invoke();

        Publish();
    }


    public IBackgroundTask Begin(string title, string? detail = null, Action? cancel = null)
    {
        var task = new RunningTask(this, title, detail, cancel);

        lock (_lock)
        {
            _running.Add(task);
        }

        Publish();

        return task;
    }


    private void End(RunningTask task)
    {
        lock (_lock)
        {
            // Idempotent: a handle disposed twice - a using inside a using, a finally after an early
            // return - must not take the count negative and hide a task that is still running.
            if (_running.Remove(task) is false)
            {
                return;
            }
        }

        Publish();
    }

    /// <summary>
    /// Reads the current state and pushes it at the bound properties, on the UI thread.
    /// </summary>
    /// <remarks>
    /// Reports arrive from wherever the work is - upload continuations, the sync engine's own threads
    /// - so this is the one place that crosses back. <see cref="Dispatcher.InvokeAsync"/> rather than
    /// Invoke, because a report must never make the thing being reported on wait for a redraw.
    /// </remarks>
    private void Publish()
    {
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Apply();

            return;
        }

        _ = dispatcher.InvokeAsync(Apply);
    }

    private void Apply()
    {
        int count;
        string title;
        string? detail;
        int completed;
        int total;
        bool cancellable;

        // Copied out under the same lock the reports are written under, so the strip can never draw
        // one task's title beside another's count. Nothing is bound inside the lock: a property
        // change raises handlers synchronously, and none of them has any business running under it.
        lock (_lock)
        {
            count = _running.Count;

            if (count == 0)
            {
                title = "";
                detail = null;
                completed = 0;
                total = 0;
                cancellable = false;
            }
            else
            {
                var current = _running[^1];

                title = current.Title;
                detail = current.Detail;
                completed = current.Completed;
                total = current.Total;
                cancellable = current.CanCancel;
            }
        }

        if (count == 0)
        {
            IsVisible = false;
            Others = null;
            CanCancel = false;

            return;
        }

        CanCancel = cancellable;
        Title = title;
        Detail = detail;
        IsIndeterminate = total <= 0;
        Progress = total > 0 ? Math.Clamp(completed * 100d / total, 0, 100) : 0;

        Others = count > 1
            ? count == 2 ? "and 1 more" : $"and {count - 1} more"
            : null;

        IsVisible = true;
    }


    /// <summary>
    /// One announced piece of work. Its fields are written from whichever thread is doing the work and
    /// read under the same lock the list is, so the strip never renders half of an update.
    /// </summary>
    private sealed class RunningTask(BackgroundTaskViewModel owner, string title, string? detail, Action? cancel)
        : IBackgroundTask
    {
        private Action? _cancel = cancel;


        public string Title { get; private set; } = title;
        public string? Detail { get; private set; } = detail;
        public int Completed { get; private set; }
        public int Total { get; private set; }

        public bool CanCancel => _cancel is not null;


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

        public void Report(string? detail, int completed, int total)
        {
            lock (owner._lock)
            {
                Detail = detail;
                Completed = completed;
                Total = total;
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

        public void Dispose() => owner.End(this);
    }
}
