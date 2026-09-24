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

    /// <summary>One-shot only, which is all anything under test asks for.</summary>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(this, () => callback(state), _now + dueTime);

        lock (_lock)
        {
            _timers.Add(timer);
        }

        return timer;
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
        public DateTimeOffset DueAt => dueAt;

        public void Fire() => fire();

        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
