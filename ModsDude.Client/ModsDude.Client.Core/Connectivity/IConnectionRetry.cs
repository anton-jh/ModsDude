using ModsDude.Client.Core.Notices;

namespace ModsDude.Client.Core.Connectivity;

public interface IConnectionRetry
{
    /// <summary>Raised when the column would say something different. Fired from whatever thread the attempt ran on.</summary>
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

    IReadOnlyList<Notice> Build();
}
