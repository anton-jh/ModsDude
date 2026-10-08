namespace ModsDude.Client.Core.Tests;

/// <summary>A clock that only moves when told, and fires its timers when it does.</summary>
internal sealed class TestTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<TestTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public int PendingTimers
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count;
            }
        }
    }

    public void Advance(TimeSpan by)
    {
        List<TestTimer> due;

        lock (_lock)
        {
            _now += by;
            due = [.. _timers.Where(x => x.DueAt <= _now)];
            _timers.RemoveAll(due.Contains);
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    /// <summary>Moves the clock to <paramref name="to"/>, firing what falls due on the way.</summary>
    public void SetUtcNow(DateTimeOffset to) => Advance(to - _now);

    /// <summary>One-shot only: a timer that wants to run again re-arms itself with <see cref="ITimer.Change"/>.</summary>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(this, () => callback(state), _now + dueTime);

        lock (_lock)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    private void Rearm(TestTimer timer, TimeSpan dueTime)
    {
        lock (_lock)
        {
            _timers.Remove(timer);

            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                timer.DueAt = _now + dueTime;
                _timers.Add(timer);
            }
        }
    }

    private void Remove(TestTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class TestTimer(TestTimeProvider owner, Action fire, DateTimeOffset dueAt) : ITimer
    {
        public DateTimeOffset DueAt { get; set; } = dueAt;

        public void Fire() => fire();

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            owner.Rearm(this, dueTime);

            return true;
        }

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
