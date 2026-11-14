namespace ModsDude.Client.Core.Sessions;

/// <summary>
/// The lifetime every long-running piece of work is bound to: one signed-in session of the app. It
/// ends when the app leaves or the account changes.
/// </summary>
public interface ISessionWork
{
    /// <summary>Cancelled when the session ends. Link every long-running operation to it.</summary>
    CancellationToken Stopping { get; }

    /// <summary>
    /// Cancels <see cref="Stopping"/>, waits for every lease to be released, tells the server nothing
    /// here is being played any more, and starts a fresh session. Concurrent calls share one stop.
    /// </summary>
    /// <returns>False where something was still running when <see cref="SessionWork.StopTimeout"/> ran out.</returns>
    Task<bool> StopAllAsync(CancellationToken cancellationToken);
}
