namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Runs <see cref="IContentStoreMaintenance.SweepAllAsync"/> in the background for the events that can
/// grow a store with no sync behind them: startup and a finished import.
/// </summary>
public interface IContentStoreTidier : IDisposable
{
    /// <summary>
    /// Starts a sweep and returns at once. A request made while one runs is coalesced into one more
    /// run after it; a request after <see cref="IDisposable.Dispose"/> does nothing.
    /// </summary>
    void Request();
}
