namespace ModsDude.Client.Wpf.Shell.Navigation;

public interface INavigationLockService : IDisposable
{
    PageViewModel? Lock { get; }

    void AcquireLock(PageViewModel page);

    void ReleaseLock(PageViewModel page);

    bool HasLock();

    void Clear();
}
