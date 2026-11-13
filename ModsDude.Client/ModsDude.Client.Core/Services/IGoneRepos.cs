using ModsDude.Client.Core.Connectivity;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// Whether a repo is gone on evidence: the server is answering and the repo list it last sent does
/// not have it. A list that has not loaded, or a server that is not answering, is never evidence.
/// </summary>
public interface IGoneRepos
{
    bool IsGone(Guid repoId);
}


public sealed class GoneRepos(IKnownRepos knownRepos, IServerConnection connection) : IGoneRepos
{
    public bool IsGone(Guid repoId) => connection.IsOnline && knownRepos.IsGone(repoId);
}
