using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Core.Sync;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Services;

/// <summary>
/// Runs <see cref="SavegameClaimWatch"/> now and then, and asks for a drift check when what it read
/// changes the answer - which is how somebody whose save was taken over hears about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whether or not the window is in sight</b>, unlike <see cref="RemoteChangeWatcher"/>. What that
/// one finds is a dot in the sidebar, worth nothing to nobody; what this one finds is a notice, which
/// becomes a Windows toast exactly when the window is not in front - and the person most in need of
/// hearing that their save was taken is the one playing it, with ModsDude behind the game.
/// </para>
/// <para>
/// <b>Failures are logged and nothing else</b>, for the reason the other watcher gives: nobody asked
/// for this, and the next tick asks again.
/// </para>
/// </remarks>
public sealed class SavegameClaimWatcher(
    SavegameClaimWatch watch,
    DriftMonitor monitor,
    RepoRepository repoRepository,
    ILogger<SavegameClaimWatcher> logger)
    : IDisposable
{
    /// <summary>One list read per repo holding a save, and none where nothing is held.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The first look, soon after start rather than a whole interval in: a takeover that happened while
    /// the app was closed is exactly what somebody opening it wants to hear about.
    /// </summary>
    private static readonly TimeSpan _firstLook = TimeSpan.FromSeconds(20);

    private DispatcherTimer? _timer;
    private bool _checking;


    public void Start()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = _firstLook
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
        // Not signed in yet, or the repos have not arrived: there is nothing to ask with, so the
        // first look waits for the next tick rather than spending the long interval.
        if (repoRepository.HasLoaded is false)
        {
            return;
        }

        _timer!.Interval = Interval;

        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_checking)
        {
            return;
        }

        _checking = true;

        try
        {
            if (await watch.RefreshAsync(CancellationToken.None))
            {
                await monitor.CheckAsync(DriftCheckReason.Background);
            }
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Could not check the server for who holds the savegames checked out here.");
        }
        finally
        {
            _checking = false;
        }
    }
}
