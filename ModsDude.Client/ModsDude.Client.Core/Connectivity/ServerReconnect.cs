using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Changes;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Connectivity;

/// <summary>
/// Gets the app back online: probes the server while it is unreachable, and once it answers, reads
/// every loaded store again and says so.
/// </summary>
/// <remarks>
/// <para>
/// <b>Somebody has to ask.</b> Offline, the app blocks everything that would send a request and the
/// change poll pauses, so without a probe nothing would ever notice the server coming back. The probe
/// is the change counters: the smallest read there is, and one that needs the same sign-in as the rest.
/// </para>
/// <para>
/// <b>The drift check runs on both edges</b>, because what it may say about held savegames and profile
/// revisions depends on whether those facts are current.
/// </para>
/// </remarks>
public sealed class ServerReconnect(
    IServerConnection connection,
    IConnectionRetry retry,
    IChangesClient changesClient,
    IChangePoll changePoll,
    IDriftMonitor driftMonitor,
    TimeProvider timeProvider,
    ILogger<ServerReconnect> logger)
    : IServerReconnect
{
    /// <summary>How long to wait before probing again after a probe failed in a way waiting does not fix.</summary>
    public static readonly TimeSpan AfterUnexpectedFailure = TimeSpan.FromMinutes(1);


    public event EventHandler? Reconnected;


    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Whatever got an answer first - the probe or anything else - every attempt still waiting out
        // its delay goes now: the probe, and the first repo load.
        connection.Changed += WakeWaitingAttempts;

        try
        {
            while (true)
            {
                await WaitUntilAsync(online: false, cancellationToken);
                await CheckDriftAsync();

                while (connection.IsOnline is false)
                {
                    await ProbeAsync(cancellationToken);
                }

                await CheckDriftAsync();
                await ReadEverythingAgainAsync(cancellationToken);

                Reconnected?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            connection.Changed -= WakeWaitingAttempts;
        }
    }


    private void WakeWaitingAttempts(object? sender, EventArgs e)
    {
        if (connection.IsOnline)
        {
            retry.RetryNow();
        }
    }


    private async Task ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await retry.RunAsync(ConnectionTarget.Server, changesClient.GetChangesV1Async, cancellationToken);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            logger.LogWarning(exception, "The server probe failed in a way waiting will not fix; probing again in {Delay}.", AfterUnexpectedFailure);

            if (connection.IsOnline is false)
            {
                await Task.Delay(AfterUnexpectedFailure, timeProvider, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task ReadEverythingAgainAsync(CancellationToken cancellationToken)
    {
        try
        {
            await changePoll.RereadAllAsync(cancellationToken);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            logger.LogInformation(exception, "Could not read everything again after the server came back; the change poll catches up.");
        }
    }

    private async Task CheckDriftAsync()
    {
        try
        {
            await driftMonitor.CheckAsync(DriftCheckReason.Background);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not check for drift after the server connection changed.");
        }
    }

    private async Task WaitUntilAsync(bool online, CancellationToken cancellationToken)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, EventArgs e)
        {
            if (connection.IsOnline == online)
            {
                reached.TrySetResult();
            }
        }

        connection.Changed += OnChanged;

        try
        {
            // After subscribing, so a change between the two cannot be missed.
            OnChanged(this, EventArgs.Empty);

            await reached.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            connection.Changed -= OnChanged;
        }
    }
}
