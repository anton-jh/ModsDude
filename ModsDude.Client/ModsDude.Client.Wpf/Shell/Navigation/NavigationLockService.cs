
namespace ModsDude.Client.Wpf.Shell.Navigation;

public class NavigationLockService : INavigationLockService
{
    public PageViewModel? Lock { get; private set; }


    public void AcquireLock(PageViewModel page)
    {
        if (Lock is not null && Lock != page)
        {
            throw new InvalidOperationException("Cannot acquire navigation lock: Taken");
        }

        Lock = page;
    }

    public void ReleaseLock(PageViewModel page)
    {
        if (Lock == page)
        {
            Lock = null;
        }
    }

    public bool HasLock()
    {
        return Lock is not null;
    }

    public void Clear()
    {
        Lock = null;
    }

    public void Dispose()
    {
        if (Lock is IDisposable disposable)
        {
            disposable.Dispose();
        }
        Clear();
    }
}
