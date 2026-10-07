namespace ModsDude.Client.Core.Connectivity;

public sealed class ServerConnection : IServerConnection
{
    private int _isOffline;


    public event EventHandler? Changed;


    public bool IsOnline => Volatile.Read(ref _isOffline) == 0;


    public void ReportReachable() => Set(offline: false);

    public void ReportUnreachable() => Set(offline: true);


    private void Set(bool offline)
    {
        var value = offline ? 1 : 0;

        if (Interlocked.Exchange(ref _isOffline, value) != value)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
