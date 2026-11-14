using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Concurrency;

namespace ModsDude.Client.Core.Sessions;

public sealed class SessionWork(
    IResourceLeases leases,
    IPresenceReporter presence,
    TimeProvider time,
    ILogger<SessionWork> logger) : ISessionWork
{
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan GoOfflineTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private CancellationTokenSource _session = new();
    private Task<bool>? _stop;


    public CancellationToken Stopping
    {
        get
        {
            lock (_lock)
            {
                return _session.Token;
            }
        }
    }


    public Task<bool> StopAllAsync(CancellationToken cancellationToken)
    {
        Task<bool> stop;

        lock (_lock)
        {
            // On the thread pool, so the cancellation callbacks never run under this lock.
            stop = _stop ??= Task.Run(StopAsync, CancellationToken.None);
        }

        return stop.WaitAsync(cancellationToken);
    }


    private async Task<bool> StopAsync()
    {
        CancellationTokenSource session;

        lock (_lock)
        {
            session = _session;
        }

        try
        {
            await session.CancelAsync();

            var stopped = await WaitForLeasesAsync();

            await GoOfflineAsync();

            return stopped;
        }
        finally
        {
            // The old source is left undisposed: work that has not noticed yet may still link to its token.
            lock (_lock)
            {
                _session = new();
                _stop = null;
            }
        }
    }

    private async Task<bool> WaitForLeasesAsync()
    {
        using var timeout = new CancellationTokenSource(StopTimeout, time);

        try
        {
            await leases.WhenIdleAsync(timeout.Token);

            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.LogWarning("Still running {Timeout} after being asked to stop: {Holders}.",
                StopTimeout, string.Join(", ", leases.DescribeAll()));

            return false;
        }
    }

    private async Task GoOfflineAsync()
    {
        using var timeout = new CancellationTokenSource(GoOfflineTimeout, time);

        try
        {
            await presence.GoOfflineAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.LogWarning("Could not tell the server within {Timeout} that nothing is being played any more.", GoOfflineTimeout);
        }
    }
}
