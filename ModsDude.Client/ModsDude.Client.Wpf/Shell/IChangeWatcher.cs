namespace ModsDude.Client.Wpf.Shell;

public interface IChangeWatcher : IDisposable
{
    void Start();

    /// <summary>Reads what changed on the server now, after any read already under way.</summary>
    Task PollNowAsync(CancellationToken cancellationToken);
}
