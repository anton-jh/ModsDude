using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.Notices;
using System.Net.Sockets;

namespace ModsDude.Client.Core.Tests.Connectivity;

/// <summary>
/// Tried again on its own until something answers, sooner when asked, and said in the column for as
/// long as it is being tried.
/// </summary>
public class ConnectionRetryTests
{
    private readonly TestTimeProvider _time = new();
    private readonly ConnectionRetry _retry;


    public ConnectionRetryTests()
    {
        _retry = new ConnectionRetry(NullLogger<ConnectionRetry>.Instance, _time);
    }


    [Fact]
    public async Task Getting_through_first_time_says_nothing()
    {
        var attempts = new Attempts(Succeed);

        Assert.True(await _retry.RunAsync(ConnectionTarget.Server, attempts.Next, CancellationToken.None));

        Assert.Equal(1, attempts.Count);
        Assert.Empty(_retry.Build());
        Assert.Equal(0, _time.PendingTimers);
    }

    [Fact]
    public async Task Nobody_answering_is_tried_again_after_the_interval_and_the_notice_goes_once_it_answers()
    {
        var attempts = new Attempts(Unreachable, Succeed);

        var run = _retry.RunAsync(ConnectionTarget.Server, attempts.Next, CancellationToken.None);

        await WaitUntil(() => _time.PendingTimers == 1);

        var notice = Assert.Single(_retry.Build());
        Assert.Equal($"{ConnectionRetry.KeyPrefix}{ConnectionTarget.Server}", notice.Key);
        Assert.Equal(NoticeSeverity.Pending, notice.Severity);
        Assert.False(notice.CanDismiss);
        Assert.Equal(NoticeActionKind.RetryConnection, Assert.Single(notice.Actions).Kind);

        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, attempts.Count);

        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, attempts.Count);
        Assert.Empty(_retry.Build());
    }

    [Fact]
    public async Task The_notice_says_how_often_it_has_tried_and_when_it_tries_next()
    {
        var attempts = new Attempts(Unreachable, Unreachable, Succeed);

        var run = _retry.RunAsync(ConnectionTarget.SignIn, attempts.Next, CancellationToken.None);

        await WaitUntil(() => _time.PendingTimers == 1);

        var next = TimeZoneInfo.ConvertTime(_time.GetUtcNow() + TimeSpan.FromSeconds(5), _time.LocalTimeZone);
        Assert.Equal($"Tried once. Trying again at {next:HH:mm:ss}.", Assert.Single(_retry.Build()).Footnote);

        _time.Advance(TimeSpan.FromSeconds(5));
        await WaitUntil(() => attempts.Count == 2 && _time.PendingTimers == 1);

        next = TimeZoneInfo.ConvertTime(_time.GetUtcNow() + TimeSpan.FromSeconds(10), _time.LocalTimeZone);
        Assert.Equal($"Tried 2 times. Trying again at {next:HH:mm:ss}.", Assert.Single(_retry.Build()).Footnote);

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void The_wait_grows_and_then_settles_at_a_minute()
    {
        var delays = Enumerable.Range(1, 7).Select(x => ConnectionRetry.DelayAfter(x).TotalSeconds);

        Assert.Equal([5, 10, 20, 30, 60, 60, 60], delays);
    }

    [Fact]
    public async Task Retry_now_cuts_the_wait_short()
    {
        var attempts = new Attempts(Unreachable, Succeed);

        var run = _retry.RunAsync(ConnectionTarget.Server, attempts.Next, CancellationToken.None);

        await WaitUntil(() => _time.PendingTimers == 1);

        _retry.RetryNow();

        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, attempts.Count);
        Assert.Equal(0, _time.PendingTimers);
    }

    [Fact]
    public async Task An_answer_that_waiting_will_not_change_is_thrown_and_takes_the_notice_with_it()
    {
        var attempts = new Attempts(Unreachable, () => throw new InvalidOperationException("Refused."));

        var run = _retry.RunAsync(ConnectionTarget.Server, attempts.Next, CancellationToken.None);

        await WaitUntil(() => _time.PendingTimers == 1);
        Assert.Single(_retry.Build());

        _retry.RetryNow();

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(_retry.Build());
    }

    [Fact]
    public async Task Cancelling_stops_trying_and_takes_the_notice_with_it()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = new Attempts(Unreachable, Succeed);

        var run = _retry.RunAsync(ConnectionTarget.Server, attempts.Next, cancellation.Token);

        await WaitUntil(() => _time.PendingTimers == 1);

        cancellation.Cancel();

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, attempts.Count);
        Assert.Empty(_retry.Build());
        Assert.Equal(0, _time.PendingTimers);
    }

    [Fact]
    public async Task A_run_winding_down_leaves_a_newer_runs_notice_alone()
    {
        using var first = new CancellationTokenSource();

        var older = _retry.RunAsync(ConnectionTarget.Server, new Attempts(Unreachable).Next, first.Token);
        await WaitUntil(() => _time.PendingTimers == 1);

        var newer = _retry.RunAsync(ConnectionTarget.Server, new Attempts(Unreachable, Succeed).Next, CancellationToken.None);
        await WaitUntil(() => _time.PendingTimers == 2);

        first.Cancel();
        Assert.False(await older.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Single(_retry.Build());

        _retry.RetryNow();
        Assert.True(await newer.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(_retry.Build());
    }

    [Fact]
    public async Task What_the_caller_also_counts_is_retried_too()
    {
        var retry = new ConnectionRetry(NullLogger<ConnectionRetry>.Instance, _time, x => x is ProviderUnreachable);
        var attempts = new Attempts(() => throw new ProviderUnreachable(), Succeed);

        var run = retry.RunAsync(ConnectionTarget.SignIn, attempts.Next, CancellationToken.None);

        await WaitUntil(() => _time.PendingTimers == 1);
        retry.RetryNow();

        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, attempts.Count);
    }


    private static void Succeed() { }

    private static void Unreachable()
        => throw new HttpRequestException("No connection could be made.", new SocketException((int)SocketError.ConnectionRefused));

    /// <summary>
    /// Polled rather than signalled: the loop's continuations run wherever the timer that fired them
    /// did, and what is being waited for is the state the retry is left in, not any one callback.
    /// </summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (condition() is false)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The retry never got there.");
            }

            await Task.Delay(10);
        }
    }


    /// <summary>One outcome per attempt, in order; the last one repeats.</summary>
    private sealed class Attempts(params Action[] outcomes)
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task Next(CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _count) - 1;

            outcomes[Math.Min(index, outcomes.Length - 1)]();

            return Task.CompletedTask;
        }
    }

    private sealed class ProviderUnreachable : Exception;
}
