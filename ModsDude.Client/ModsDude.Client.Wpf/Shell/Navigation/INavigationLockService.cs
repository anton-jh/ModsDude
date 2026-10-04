namespace ModsDude.Client.Wpf.Shell.Navigation;

/// <summary>
/// Held by whatever has unsaved changes - a page, or a form on one - so that navigating away asks first.
/// </summary>
public interface INavigationLockService : IDisposable
{
    void AcquireLock(object owner);

    void ReleaseLock(object owner);

    bool HasLock();

    void Clear();
}
