namespace ModsDude.Client.Core.Persistence;

/// <summary>
/// The persisted local state. It is only ever touched under one lock - read through
/// <see cref="Read"/>, changed and written through <see cref="Update"/> - so a write can never
/// serialise a collection another thread is changing.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// Reads under the lock. What <paramref name="read"/> returns must not be a live collection of
    /// the state - copy it - or it can be changed while the caller enumerates it.
    /// </summary>
    TResult Read<TResult>(Func<LocalState, TResult> read);

    /// <summary>Changes the state and writes it, under the lock.</summary>
    void Update(Action<LocalState> update);

    /// <summary>Changes the state, and writes it only where <paramref name="update"/> says it changed.</summary>
    /// <returns>What <paramref name="update"/> returned.</returns>
    bool UpdateIf(Func<LocalState, bool> update);
}
