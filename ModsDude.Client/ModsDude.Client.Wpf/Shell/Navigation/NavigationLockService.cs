namespace ModsDude.Client.Wpf.Shell.Navigation;

public class NavigationLockService : INavigationLockService
{
    private object? _owner;


    public void AcquireLock(object owner)
    {
        if (_owner is not null && _owner != owner)
        {
            throw new InvalidOperationException("Cannot acquire navigation lock: Taken");
        }

        _owner = owner;
    }

    public void ReleaseLock(object owner)
    {
        if (_owner == owner)
        {
            _owner = null;
        }
    }

    public bool HasLock()
    {
        return _owner is not null;
    }

    public void Clear()
    {
        _owner = null;
    }

    public void Dispose()
    {
        if (_owner is IDisposable disposable)
        {
            disposable.Dispose();
        }
        Clear();
    }
}
