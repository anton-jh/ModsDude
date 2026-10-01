using ModsDude.Client.Core.Updates;

namespace ModsDude.Client.Wpf.Shell.Updates;

public interface IAppUpdater : IUpdateStatus, IDisposable
{
    bool IsInstalled { get; }

    UpdateStage Stage { get; }

    /// <summary>The version this copy is: the installed package's, or the assembly's for a build that is not installed.</summary>
    string CurrentVersion { get; }

    /// <summary>One line for Settings.</summary>
    string StatusText { get; }

    bool CanCheck { get; }

    /// <summary>Starts the periodic check. Nothing to start for a copy that is not installed.</summary>
    void Start();

    /// <summary>
    /// Looks for a newer version and downloads it. Safe to call whenever: overlapping calls are dropped,
    /// and it never throws.
    /// </summary>
    Task CheckAsync();

    /// <summary>
    /// Installs a version that was downloaded on an earlier run, if there is one, by restarting into it.
    /// </summary>
    /// <remarks>
    /// <b>Only ever called by the first instance, before anything is on screen.</b> That is the one moment
    /// nothing can be running and nobody asked: a launch that finds the app already going never gets this
    /// far, so a second click on the shortcut brings the window forward instead of replacing the app under
    /// somebody who is playing. The arguments are passed on so a start at sign-in comes back up in the tray.
    /// </remarks>
    /// <returns>True where the process is on its way out, and nothing more should be started.</returns>
    bool ApplyPendingAtStartup(string[] arguments);
}
