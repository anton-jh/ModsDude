using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Repos;

/// <summary>
/// The request that created a repo, so a repeat of it is answered with that repo instead of creating
/// a second one.
/// </summary>
public class RepoCreateRequest
{
    // ef
    private RepoCreateRequest() { }

    public RepoCreateRequest(UserId userId, RepoCreateRequestId requestId, RepoId repoId, DateTime at)
    {
        UserId = userId;
        RequestId = requestId;
        RepoId = repoId;
        At = at;
    }


    public UserId UserId { get; private set; }
    public RepoCreateRequestId RequestId { get; private set; }
    public RepoId RepoId { get; private set; }
    public DateTime At { get; private set; }
}


/// <summary>Chosen by the client and repeated with a retry. Identifies the request, never an entity.</summary>
public readonly record struct RepoCreateRequestId(Guid Value);
