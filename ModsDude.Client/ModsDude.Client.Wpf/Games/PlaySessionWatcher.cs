using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.Repos;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Sync;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Games;

/// <summary>
/// Looks at which games are running every few seconds, then lets everything that depends on it act:
/// <see cref="PlaySessionWatch"/> asks for a drift check when a game closes with a checked-out
/// savegame played in it - so the notice is current by the time somebody clicks the reminder - and
/// <see cref="PresenceReporter"/> tells friends what is being played.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whether or not the window is in sight</b>, for the reason <see cref="Shell.ChangeWatcher"/>
/// gives: the person this is for has just been in the game, with ModsDude behind it or in the tray.
/// </para>
/// <para>
/// <b>Failures are logged and nothing else.</b> Nobody asked for this look, and the next one looks again.
/// </para>
/// </remarks>
public sealed class PlaySessionWatcher(
    IGameRunningMonitor runningGames,
    IPlaySessionWatch watch,
    IPresenceReporter presence,
    IDriftMonitor monitor,
    IRepoStore repoStore,
    ILogger<PlaySessionWatcher> logger)
    : IPlaySessionWatcher
{
    /// <summary>Soon enough after the game closes that the reminder arrives while somebody is still at the machine.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stopping = new();
    private DispatcherTimer? _timer;


    public void Start()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
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


    /// <remarks>The timer is stopped while a look runs, so looks never overlap.</remarks>
    private async void OnTick(object? sender, EventArgs e)
    {
        // Which process is which game is read off the repos' adapters, so until they have loaded a
        // running game would read as closed - and the first look is the one that has to know whether
        // it was already running when the app started.
        if (repoStore.HasLoaded is false)
        {
            return;
        }

        var timer = (DispatcherTimer)sender!;
        timer.Stop();

        try
        {
            runningGames.Poll();

            // The watch raises its reminder before this returns, so the drift check below cannot put
            // the same notice up as news first.
            if ((await watch.PollAsync(_stopping.Token)).Count > 0)
            {
                await monitor.CheckAsync(DriftCheckReason.Explicit);
            }

            await presence.ReportAsync(_stopping.Token);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Could not check which games are running.");
        }

        timer.Start();
    }
}
