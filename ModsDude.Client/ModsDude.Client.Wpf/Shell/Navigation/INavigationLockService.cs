namespace ModsDude.Client.Wpf.Shell.Navigation;

/// <summary>
/// Held by whatever has unsaved changes - a page, or a form on one - so that navigating away asks first.
/// </summary>
public interface INavigationLockService : IDisposable
{
    void AcquireLock(object owner);

    void ReleaseLock(object owner);

    /// <summary>Acquires the lock for <paramref name="owner"/> while <paramref name="held"/>, releases it otherwise.</summary>
    void SetLock(object owner, bool held);

    bool HasLock();

    void Clear();
}
