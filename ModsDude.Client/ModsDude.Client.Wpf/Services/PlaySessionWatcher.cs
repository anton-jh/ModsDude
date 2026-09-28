using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Services;

/// <summary>
/// Runs <see cref="PlaySessionWatch"/> every few seconds, and asks for a drift check when a game
/// closes with a checked-out savegame played in it - so the notice is current by the time somebody
/// clicks the reminder.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whether or not the window is in sight</b>, for the reason <see cref="SavegameClaimWatcher"/>
/// gives: the person this is for has just been in the game, with ModsDude behind it or in the tray.
/// </para>
/// <para>
/// <b>Costs nothing while nothing is checked out</b> - the watch asks for no process list then - and
/// a process list otherwise, which is cheap enough to ask for this often.
/// </para>
/// </remarks>
public sealed class PlaySessionWatcher(
    PlaySessionWatch watch,
    DriftMonitor monitor,
    RepoRepository repoRepository,
    ILogger<PlaySessionWatcher> logger)
    : IDisposable
{
    /// <summary>Soon enough after the game closes that the reminder arrives while somebody is still at the machine.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private DispatcherTimer? _timer;
    private bool _polling;


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
    }


    private void OnTick(object? sender, EventArgs e)
    {
        // Which process is which game is read off the repos' adapters, so until they have loaded a
        // running game would read as closed - and the first look is the one that has to know whether
        // it was already running when the app started.
        if (repoRepository.HasLoaded is false)
        {
            return;
        }

        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;

        try
        {
            // The watch raises its reminder before this returns, so the drift check below cannot put
            // the same notice up as news first.
            if ((await watch.PollAsync(CancellationToken.None)).Count > 0)
            {
                await monitor.CheckAsync(DriftCheckReason.Explicit);
            }
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Could not check which games are running.");
        }
        finally
        {
            _polling = false;
        }
    }
}
