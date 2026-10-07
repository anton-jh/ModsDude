namespace ModsDude.Client.Core.Connectivity;

public interface IServerReconnect
{
    /// <summary>Raised once the server answers again and every loaded store has been read again. Fired off the UI thread.</summary>
    event EventHandler? Reconnected;

    /// <summary>Each time the server stops answering, probes it until it does, then reads everything again. Runs until cancelled.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
