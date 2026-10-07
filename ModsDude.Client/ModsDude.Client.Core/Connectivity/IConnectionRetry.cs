using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Core.Connectivity;

public interface IConnectionRetry
{
    /// <summary>Raised when an outage starts, changes or ends. Fired from whatever thread the attempt ran on.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Runs <paramref name="attempt"/> until it gets through.
    /// </summary>
    /// <returns>True once it has; false where <paramref name="cancellationToken"/> stopped it first.</returns>
    /// <exception cref="Exception">Whatever the attempt threw that was not a connection failure.</exception>
    Task<bool> RunAsync(
        ConnectionTarget target,
        Func<CancellationToken, Task> attempt,
        CancellationToken cancellationToken);

    /// <summary>Cuts short every wait in progress, so each one tries again straight away.</summary>
    void RetryNow();

    /// <summary>How the current attempts at <paramref name="target"/> are going, or null where nothing is failing.</summary>
    ConnectionOutage? GetOutage(ConnectionTarget target);

    /// <summary>The sign-in outage as a notice. The server's is the offline state instead.</summary>
    IReadOnlyList<Notice> Build();
}
