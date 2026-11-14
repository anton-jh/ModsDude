using Microsoft.Extensions.Logging;
using ModsDude.Client.Wpf.Shell.Tray;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ModsDude.Client.Wpf.Shell;

public sealed class AppLifetime(
    Lazy<MainWindow> window,
    ISessionEnd sessionEnd,
    ILogger<AppLifetime> logger) : IAppLifetime
{
    /// <summary>The leave being asked about or carried out. Touched only on the UI thread.</summary>
    private Task? _leaving;


    public Task QuitAsync() => LeaveAsync(new("Quit?", "Stop and quit", "Quitting"), handOver: null);

    public Task RestartAsync() => LeaveAsync(new("Restart?", "Stop and restart", "Restarting"), StartNewCopy);

    public Task RestartIntoUpdateAsync(Action handOverToUpdater)
        => LeaveAsync(new("Restart to update?", "Stop and restart", "Restarting to update"), handOverToUpdater);

    public void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        var handle = new WindowInteropHelper(window.Value).EnsureHandle();

        if (sessionEnd.DescribeAtStake() is { Count: > 0 } atStake)
        {
            logger.LogInformation("Refused to let Windows end the session: {AtStake}.", string.Join(", ", atStake));

            ShutdownBlockReasonCreate(handle, $"{Core.AppIdentity.DisplayName}: {string.Join(", ", atStake)}");
            e.Cancel = true;

            return;
        }

        ShutdownBlockReasonDestroy(handle);
        window.Value.AllowClose();
    }


    /// <summary>A second request while one is pending joins it rather than asking again.</summary>
    private Task LeaveAsync(Leave leave, Action? handOver)
    {
        if (_leaving is { IsCompleted: false })
        {
            window.Value.ShowFromTray();

            return _leaving;
        }

        return _leaving = RunLeaveAsync(leave, handOver);
    }

    private async Task RunLeaveAsync(Leave leave, Action? handOver)
    {
        try
        {
            if (await sessionEnd.ConfirmAsync(leave.Title, leave.ConfirmText) is false)
            {
                return;
            }

            if (await sessionEnd.StopAsync() is false)
            {
                logger.LogWarning("Leaving with work that did not stop in time.");
            }

            logger.LogInformation("{Leave} at the user's request.", leave.LogText);

            window.Value.AllowClose();
            handOver?.Invoke();

            Application.Current.Shutdown();
        }
        finally
        {
            _leaving = null;
        }
    }

    private static void StartNewCopy()
    {
        // Started before the shutdown rather than after a process that no longer exists. It waits
        // for this one to exit before it claims the single-instance lock.
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, SingleInstance.RestartArgumentFor(Environment.ProcessId))
        {
            UseShellExecute = false
        });
    }


    private sealed record Leave(string Title, string ConfirmText, string LogText);


    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShutdownBlockReasonCreate(IntPtr hwnd, string reason);

    [DllImport("user32.dll")]
    private static extern bool ShutdownBlockReasonDestroy(IntPtr hwnd);
}
