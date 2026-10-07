using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Changes;
using ModsDude.Client.Core.Repos;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Shell;

/// <summary>
/// Runs <see cref="ChangePoll"/> every minute, and once as soon as the repo list first arrives.
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
/// </remarks>
public sealed class ChangeWatcher(
    IChangePoll poll,
    IRepoStore repoStore,
    ILogger<ChangeWatcher> logger)
    : IChangeWatcher
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>How often to look for the repo list before it has first arrived.</summary>
    private static readonly TimeSpan _untilLoaded = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _stopping = new();
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

        try
        {
            await poll.PollAsync(_stopping.Token);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Could not read the server's change counters.");
        }

        timer.Start();
    }
}
