using Microsoft.Extensions.Logging;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Owns the background sweeps: one at a time, failures logged rather than lost, and cancelled when the
/// app shuts down.
/// </summary>
/// <remarks>
/// Shutdown cancels without waiting. A sweep deletes one content-addressed file at a time and checks
/// the token between them, so stopping anywhere leaves a valid store.
/// </remarks>
public sealed class ContentStoreTidier(
    IContentStoreMaintenance maintenance,
    ILogger<ContentStoreTidier> logger)
    : IContentStoreTidier
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stopping = new();
    private Task _run = Task.CompletedTask;
    private bool _running;
    private bool _owed;
    private bool _disposed;


    /// <summary>The run in progress, or a completed task when there is none. Never faults.</summary>
    internal Task Current
    {
        get
        {
            lock (_lock)
            {
                return _run;
            }
        }
    }


    public void Request()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_running)
            {
                _owed = true;

                return;
            }

            _running = true;

            var cancellationToken = _stopping.Token;

            _run = Task.Run(() => RunAsync(cancellationToken), CancellationToken.None);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stopping.Cancel();
        _stopping.Dispose();
    }


    private async Task RunAsync(CancellationToken cancellationToken)
    {
        do
        {
            try
            {
                await maintenance.SweepAllAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A store that could not be tidied is only a store that is still too big; the next
                // request tries again.
                logger.LogWarning(exception, "Could not tidy the content stores in the background.");
            }
        }
        while (TakeOwedRun());
    }

    private bool TakeOwedRun()
    {
        lock (_lock)
        {
            _running = _owed && _disposed is false;
            _owed = false;

            return _running;
        }
    }
}
