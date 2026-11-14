using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Concurrency;
using ModsDude.Client.Core.Sessions;

namespace ModsDude.Client.Core.Tests.Sessions;

public class SessionWorkTests
{
    private const string _key = "target:a";


    [Fact]
    public async Task Stopping_cancels_the_session_and_starts_a_fresh_one()
    {
        var harness = new Harness();
        var before = harness.Work.Stopping;

        Assert.True(await harness.Work.StopAllAsync(CancellationToken.None));

        Assert.True(before.IsCancellationRequested);
        Assert.False(harness.Work.Stopping.IsCancellationRequested);
    }

    [Fact]
    public async Task Stopping_waits_for_work_that_stops_when_cancelled()
    {
        var harness = new Harness();
        var lease = harness.Leases.TryAcquireExclusive(_key, "applying")!;
        harness.Work.Stopping.Register(lease.Dispose);

        Assert.True(await harness.Work.StopAllAsync(CancellationToken.None));
        Assert.False(harness.Leases.IsHeld(_key));
    }

    [Fact]
    public async Task Work_that_does_not_stop_in_time_is_reported_and_left_running()
    {
        var harness = new Harness();
        using var lease = harness.Leases.TryAcquireExclusive(_key, "applying");

        var stop = harness.Work.StopAllAsync(CancellationToken.None);
        await harness.WaitForTimerAsync();
        harness.Time.Advance(SessionWork.StopTimeout);

        Assert.False(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(harness.Leases.IsHeld(_key));
        Assert.False(harness.Work.Stopping.IsCancellationRequested);
    }

    [Fact]
    public async Task The_server_is_told_nothing_is_played_only_after_the_work_has_stopped()
    {
        var harness = new Harness();
        var lease = harness.Leases.TryAcquireExclusive(_key, "applying")!;
        harness.Work.Stopping.Register(() =>
        {
            harness.Events.Add("released");
            lease.Dispose();
        });

        await harness.Work.StopAllAsync(CancellationToken.None);

        Assert.Equal(["released", "offline"], harness.Events);
    }

    [Fact]
    public async Task Concurrent_stops_share_one_stop()
    {
        var harness = new Harness();
        var lease = harness.Leases.TryAcquireExclusive(_key, "applying")!;

        var first = harness.Work.StopAllAsync(CancellationToken.None);
        var second = harness.Work.StopAllAsync(CancellationToken.None);
        lease.Dispose();

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([true, true], results);
        Assert.Equal(["offline"], harness.Events);
    }

    [Fact]
    public async Task A_stop_after_a_stop_stops_the_new_session()
    {
        var harness = new Harness();

        await harness.Work.StopAllAsync(CancellationToken.None);
        var second = harness.Work.Stopping;
        await harness.Work.StopAllAsync(CancellationToken.None);

        Assert.True(second.IsCancellationRequested);
        Assert.Equal(["offline", "offline"], harness.Events);
    }

    [Fact]
    public async Task A_server_that_does_not_answer_does_not_hold_up_the_stop()
    {
        var harness = new Harness();
        harness.Presence.Hang = true;

        var stop = harness.Work.StopAllAsync(CancellationToken.None);
        await harness.WaitForTimerAsync();
        harness.Time.Advance(SessionWork.GoOfflineTimeout);

        Assert.True(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_caller_giving_up_does_not_stop_the_stop()
    {
        var harness = new Harness();
        var lease = harness.Leases.TryAcquireExclusive(_key, "applying")!;
        using var cancellation = new CancellationTokenSource();

        var abandoned = harness.Work.StopAllAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        var joined = harness.Work.StopAllAsync(CancellationToken.None);
        lease.Dispose();

        Assert.True(await joined.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(["offline"], harness.Events);
    }


    private sealed class Harness
    {
        public Harness()
        {
            Presence = new FakePresence(Events);
            Work = new SessionWork(Leases, Presence, Time, NullLogger<SessionWork>.Instance);
        }

        public ResourceLeases Leases { get; } = new();
        public TestTimeProvider Time { get; } = new();
        public List<string> Events { get; } = [];
        public FakePresence Presence { get; }
        public SessionWork Work { get; }

        public async Task WaitForTimerAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

            while (Time.PendingTimers == 0)
            {
                Assert.True(DateTime.UtcNow < deadline, "The stop never started waiting.");

                await Task.Delay(10);
            }
        }
    }

    private sealed class FakePresence(List<string> events) : IPresenceReporter
    {
        public bool Hang { get; set; }

        public Task ReportAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task GoOfflineAsync(CancellationToken cancellationToken)
        {
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            lock (events)
            {
                events.Add("offline");
            }
        }

        public void ClearUserState() { }
    }
}
