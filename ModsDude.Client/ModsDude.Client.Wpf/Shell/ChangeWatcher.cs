using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Changes;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Sessions;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Shell;

/// <summary>
/// Runs <see cref="ChangePoll"/> every minute, once as soon as the repo list first arrives, and
/// whenever somebody needs it current - see <see cref="PollNowAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whether or not the window is in sight.</b> What it finds can become a notice, which becomes a
/// Windows toast exactly when the window is not in front - and the person most in need of hearing
/// that their save was taken, or that a friend started one, is in the game with ModsDude behind it.
/// A poll where nothing moved is one small read.
/// </para>
/// <para>
/// <b>Failures are logged and nothing else.</b> Nobody asked for this read, and the next one asks again.
/// </para>
/// <para>
/// <b>One read at a time</b>, whoever asked: two reads refreshing the same stores at once could each
/// leave behind the other's older answer.
/// </para>
/// </remarks>
public sealed class ChangeWatcher(
    IChangePoll poll,
    IRepoStore repoStore,
    ISessionWork session,
    ILogger<ChangeWatcher> logger)
    : IChangeWatcher
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>How often to look for the repo list before it has first arrived.</summary>
    private static readonly TimeSpan _untilLoaded = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _polling = new(1, 1);
    private DispatcherTimer? _timer;


    public void Start()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = _untilLoaded
        };

        _timer.Tick += OnTick;
        _timer.Start();
    }

    public async Task PollNowAsync(CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, session.Stopping, cancellationToken);

        await PollAsync(stop.Token);
    }

    public void Dispose()
    {
        _timer?.Stop();
        _stopping.Cancel();
        _stopping.Dispose();
    }


    /// <remarks>The timer is stopped while a poll runs, so polls never overlap.</remarks>
    private async void OnTick(object? sender, EventArgs e)
    {
        // Not signed in yet, or the shell's first load is not in: that load owns the list until then.
        if (repoStore.HasLoaded is false)
        {
            return;
        }

        var timer = (DispatcherTimer)sender!;
        timer.Stop();
        timer.Interval = Interval;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, session.Stopping);

        try
        {
            await PollAsync(stop.Token);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            logger.LogDebug("A change poll was stopped because the session ended.");
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Could not read the server's change counters.");
        }

        timer.Start();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        await _polling.WaitAsync(cancellationToken);

        try
        {
            await poll.PollAsync(cancellationToken);
        }
        finally
        {
            _polling.Release();
        }
    }
}
