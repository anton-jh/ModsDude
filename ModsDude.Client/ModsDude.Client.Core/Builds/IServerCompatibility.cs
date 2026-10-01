namespace ModsDude.Client.Core.Builds;

public interface IServerCompatibility
{
    /// <summary>Null while the server accepts this build.</summary>
    BuildMismatch? Mismatch { get; }

    /// <summary>Raised, from any thread, when <see cref="Mismatch"/> changed.</summary>
    event EventHandler? Changed;

    void ReportAccepted();

    void ReportRefused(BuildNumber server);
}


public sealed record BuildMismatch(BuildNumber Client, BuildNumber Server)
{
    /// <summary>
    /// The server is a newer build, so this copy has to update. Otherwise the server is behind: being
    /// deployed, rolled back, or this is a local build pointed at a deployed server.
    /// </summary>
    public bool ClientIsBehind => Client.Value < Server.Value;
}
