namespace ModsDude.Client.Wpf.Shell;

/// <summary>
/// Every way out of the app. Each asks first where anything would be stopped or lost, stops running
/// work before the process goes, and does nothing if the user would rather keep working.
/// </summary>
public interface IAppLifetime
{
    Task QuitAsync();

    /// <summary>Quits, and starts this copy of the app again once this process has exited.</summary>
    Task RestartAsync();

    /// <summary>Quits, handing the process over to the updater on the way out.</summary>
    Task RestartIntoUpdateAsync(Action handOverToUpdater);

    /// <summary>
    /// Windows asking whether it may end the session. Refused, with a reason Windows shows, while
    /// anything is at stake; the user can still force it from there.
    /// </summary>
    void OnSessionEnding(System.Windows.SessionEndingCancelEventArgs e);
}
