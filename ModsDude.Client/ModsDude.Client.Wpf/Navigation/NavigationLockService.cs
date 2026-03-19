using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.Navigation;

public class NavigationLockService
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
}
