namespace ModsDude.Client.Wpf.Shell;

/// <summary>
/// Leaving the app on purpose. Both ask first where work is still running, and do nothing if the
/// user would rather it kept working.
/// </summary>
public interface IAppLifetime
{
    Task QuitAsync();

    /// <summary>Quits, and starts this copy of the app again once this process has exited.</summary>
    Task RestartAsync();
}
