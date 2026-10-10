using Microsoft.Extensions.Logging;
using ModsDude.Client.Wpf.Shell.Tray;
using System.Diagnostics;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell;

public sealed class AppLifetime(Lazy<MainWindow> window, ILogger<AppLifetime> logger) : IAppLifetime
{
    public async Task QuitAsync()
    {
        if (await window.Value.PrepareToLeaveAsync() is false)
        {
            return;
        }

        logger.LogInformation("Quitting at the user's request.");

        Application.Current.Shutdown();
    }

    public async Task RestartAsync()
    {
        if (await window.Value.PrepareToLeaveAsync() is false)
        {
            return;
        }

        logger.LogInformation("Restarting at the user's request.");

        // The new copy waits for this one to let go of the single-instance lock, so it is started
        // before the shutdown rather than after a process that no longer exists.
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, SingleInstance.RestartArgument)
        {
            UseShellExecute = false
        });

        Application.Current.Shutdown();
    }
}
