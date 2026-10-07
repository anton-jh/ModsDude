namespace ModsDude.Client.Core.Connectivity;

/// <summary>
/// Whether the ModsDude server answered the last request sent to it. The app is useless without it,
/// so while it does not, the app shows the offline state and blocks everything that needs it.
/// </summary>
public interface IServerConnection
{
    /// <summary>True until a request finds nobody answering, and again once one gets an answer.</summary>
    bool IsOnline { get; }

    /// <summary>Raised when <see cref="IsOnline"/> changes. Fired from whatever thread the request ran on.</summary>
    event EventHandler? Changed;

    void ReportReachable();

    void ReportUnreachable();
}
