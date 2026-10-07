namespace ModsDude.Client.Core.Stores;

/// <summary>
/// The one thread a store's state changes on, which is the thread everything bound to it reads from.
/// </summary>
public interface IStoreDispatcher
{
    /// <summary>Runs <paramref name="action"/> on that thread: at once where the caller is already on it.</summary>
    Task InvokeAsync(Action action);
}
