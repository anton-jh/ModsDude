using Microsoft.Win32;
using ModsDude.Client.Core.Sync;
using System.Windows;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Tray;

/// <summary>
/// The drift checks that do not depend on anybody touching the window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> The check runs at startup and on window activation, and the watcher
/// covers the folder changing while the app is looking. That was enough for an app that is open when
/// somebody is using it. An app that lives in the tray is never activated, and a watcher observes
/// nothing across a night of sleep - which is precisely when the game's updater is most likely to
/// have run.
/// </para>
/// <para>
/// Everything here is a <see cref="DriftCheckReason.Background"/> check, so the monitor's own
/// throttle stops a resume that fires three events from costing three listings. A resume also
/// rebuilds the watchers: one on a network path or a drive that came back does not always survive
/// the trip, and a watcher that silently stopped is worse than none.
/// </para>
/// </remarks>
public sealed class DriftBackstop(DriftMonitor monitor) : IDisposable
{
    /// <summary>
    /// A directory listing per folder and a hash per held savegame slot, so this is not a loop to run
    /// every few seconds - but a mod folder that changed an hour ago and was never noticed is the
    /// failure it is there to prevent, and ten minutes keeps that to a coffee.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private DispatcherTimer? _timer;


    public void Start()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };

        _timer.Tick += (_, _) => _ = monitor.CheckAsync(DriftCheckReason.Background);
        _timer.Start();

        // Static events: they keep this alive until they are removed, which is what Dispose is for.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Dispose()
    {
        _timer?.Stop();

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }


    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Resume)
        {
            Wake();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
        {
            Wake();
        }
    }

    /// <summary>
    /// These events arrive on a thread that is not the UI's, and the watchers are only ever touched
    /// from it.
    /// </summary>
    private void Wake()
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            monitor.Watch();

            _ = monitor.CheckAsync(DriftCheckReason.Background);
        });
    }
}
