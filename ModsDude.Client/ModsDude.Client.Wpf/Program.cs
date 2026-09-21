using Microsoft.Win32;
using ModsDude.Client.Core;
using ModsDude.Client.Core.Startup;
using ModsDude.Client.Wpf.Tray;
using Velopack;

namespace ModsDude.Client.Wpf;

/// <summary>
/// The real entry point, ahead of WPF's own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Velopack has to run first.</b> The installer and the updater start the app with special arguments to
/// run a hook - after an install, before an uninstall - and expect it to do that and exit. If WPF were
/// allowed to start up, every one of those would open a window. For a normal start <c>Run</c> returns at
/// once and does nothing, which is also what makes this harmless for a copy that was never installed.
/// </para>
/// <para>
/// <b>Why there is a <c>Main</c> at all:</b> WPF generates one from <c>App.xaml</c>, and there is no way to
/// put code in front of it. <c>App.xaml</c> is therefore built as a page and the project names this class as
/// its startup object.
/// </para>
/// </remarks>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build()
            // Off, because "at the next start" is not the same thing as "when nothing is running": a
            // second launch while the app sits in the tray is a start too, and letting Velopack apply
            // there replaces and restarts the running copy with no question asked. The app applies a
            // downloaded update itself, as the first instance and before it shows anything - see
            // AppUpdater.ApplyPendingAtStartup.
            .SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => RemoveWhatTheAppLeftOutsideItsFolder())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }


    /// <summary>
    /// Takes back what the app put on the machine that the uninstaller does not know about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The installer removes its own folder and its own shortcuts. What is left is the start-with-Windows
    /// entry, which would otherwise keep launching an exe that no longer exists at every sign-in, and the
    /// notification identity the app registered.
    /// </para>
    /// <para>
    /// <b>The user's data stays.</b> Settings, sign-in and the content stores are the user's, and someone
    /// who reinstalls should find their games where they left them; a cache the size of a game library is
    /// theirs to delete, not this to guess at. Each step is on its own so one that fails takes none of the
    /// others with it, and none of it may stop the uninstall.
    /// </para>
    /// </remarks>
    private static void RemoveWhatTheAppLeftOutsideItsFolder()
    {
        var name = AppIdentity.NameFor(AppIdentity.ProductionEnvironment);

        Try(() =>
        {
            var startup = new RegistryStartupRegistry();

            startup.RemoveRunCommand(name);
            startup.RemoveApproval(name);
        });

        Try(() => Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{name}", throwOnMissingSubKey: false));

        Try(() => StartMenuShortcut.Remove(AppIdentity.DisplayNameFor(AppIdentity.ProductionEnvironment)));
    }

    private static void Try(Action step)
    {
        try
        {
            step();
        }
        catch (Exception)
        {
            // Nothing to report to: this runs inside an uninstall with no window and no log.
        }
    }
}
