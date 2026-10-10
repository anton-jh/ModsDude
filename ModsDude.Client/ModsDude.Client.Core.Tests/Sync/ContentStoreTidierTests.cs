using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

public class ContentStoreTidierTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);


    [Fact]
    public async Task A_failed_sweep_is_logged_and_the_next_request_sweeps_again()
    {
        var failure = new InvalidOperationException("broken store");
        var maintenance = new FakeMaintenance((call, _) => call == 1 ? throw failure : Task.FromResult(0L));
        var logger = new RecordingLogger<ContentStoreTidier>();
        using var tidier = new ContentStoreTidier(maintenance, logger);

        tidier.Request();
        await tidier.Current.WaitAsync(_timeout);

        var logged = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Same(failure, logged.Exception);

        tidier.Request();
        await tidier.Current.WaitAsync(_timeout);

        Assert.Equal(2, maintenance.Calls);
    }

    [Fact]
    public async Task Dispose_cancels_a_running_sweep_without_logging_a_failure()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken seen = default;
        var maintenance = new FakeMaintenance(async (_, cancellationToken) =>
        {
            seen = cancellationToken;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return 0;
        });
        var logger = new RecordingLogger<ContentStoreTidier>();
        var tidier = new ContentStoreTidier(maintenance, logger);

        tidier.Request();
        await started.Task.WaitAsync(_timeout);
        var running = tidier.Current;

        tidier.Dispose();
        await running.WaitAsync(_timeout);

        Assert.True(seen.IsCancellationRequested);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Concurrent_requests_during_a_sweep_coalesce_into_one_more_run()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenance = new FakeMaintenance(async (call, _) =>
        {
            if (call == 1)
            {
                started.SetResult();
                await release.Task;
            }

            return 0;
        });
        using var tidier = new ContentStoreTidier(maintenance, new RecordingLogger<ContentStoreTidier>());

        tidier.Request();
        await started.Task.WaitAsync(_timeout);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(tidier.Request)));

        release.SetResult();
        await tidier.Current.WaitAsync(_timeout);

        Assert.Equal(2, maintenance.Calls);
    }

    [Fact]
    public async Task A_run_owed_when_the_app_shuts_down_is_dropped()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenance = new FakeMaintenance(async (_, _) =>
        {
            // Ignores the token, so the run finishes normally after the dispose.
            started.TrySetResult();
            await release.Task;

            return 0;
        });
        var tidier = new ContentStoreTidier(maintenance, new RecordingLogger<ContentStoreTidier>());

        tidier.Request();
        await started.Task.WaitAsync(_timeout);
        tidier.Request();
        var running = tidier.Current;

        tidier.Dispose();
        release.SetResult();
        await running.WaitAsync(_timeout);

        Assert.Equal(1, maintenance.Calls);
    }

    [Fact]
    public async Task A_request_after_dispose_does_nothing()
    {
        var maintenance = new FakeMaintenance((_, _) => Task.FromResult(0L));
        var tidier = new ContentStoreTidier(maintenance, new RecordingLogger<ContentStoreTidier>());

        tidier.Dispose();
        tidier.Request();
        await tidier.Current.WaitAsync(_timeout);

        Assert.Equal(0, maintenance.Calls);
    }


    private sealed class FakeMaintenance(Func<int, CancellationToken, Task<long>> sweep) : IContentStoreMaintenance
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<long> SweepAllAsync(CancellationToken cancellationToken)
            => sweep(Interlocked.Increment(ref _calls), cancellationToken);

        public IReadOnlyList<ContentStore> GetStores() => throw new NotSupportedException();

        public Task<ContentStoreClearResult> ReclaimAsync(ContentStore store, CancellationToken cancellationToken, Action? onWaiting = null)
            => throw new NotSupportedException();

        public Task<long?> RecycleQuarantineAsync(ContentStore store, CancellationToken cancellationToken, Action? onWaiting = null)
            => throw new NotSupportedException();

        public Task<StoreVerificationReport> VerifyAsync(
            ContentStore store,
            IProgress<ContentStoreVerificationProgress>? progress,
            CancellationToken cancellationToken,
            Action? onWaiting = null)
            => throw new NotSupportedException();
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly Lock _lock = new();
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock)
            {
                _entries.Add(new LogEntry(logLevel, exception));
            }
        }
    }
}
