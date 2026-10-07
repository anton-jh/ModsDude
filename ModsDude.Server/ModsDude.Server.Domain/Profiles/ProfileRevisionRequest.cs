using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// The latest request one person made that writes a revision of one profile - a save or a restore -
/// and the revision it was answered with, so a repeat of it is given the same answer instead of
/// being refused as stale or minting a second revision.
/// </summary>
/// <remarks>
/// One per person per profile: a client only ever repeats its latest request, so a newer one
/// replaces the row rather than adding to it.
/// </remarks>
public class ProfileRevisionRequest
{
    // ef
    private ProfileRevisionRequest() { }

    public ProfileRevisionRequest(
        RepoId repoId,
        ProfileId profileId,
        UserId userId,
        ProfileRevisionRequestId requestId,
        DateTime at,
        RevisionNumber answeredWith)
    {
        RepoId = repoId;
        ProfileId = profileId;
        UserId = userId;
        Replace(requestId, at, answeredWith);
    }


    public RepoId RepoId { get; private set; }
    public ProfileId ProfileId { get; private set; }
    public UserId UserId { get; private set; }

    public ProfileRevisionRequestId RequestId { get; private set; }
    public DateTime At { get; private set; }

    /// <summary>The revision the request was answered with: the one it minted, or the head where nothing changed.</summary>
    public RevisionNumber AnsweredWith { get; private set; }


    public bool Answers(ProfileRevisionRequestId requestId) => RequestId == requestId;

    /// <summary>Records a newer request by the same person, which is the only one they can still repeat.</summary>
    public void Replace(ProfileRevisionRequestId requestId, DateTime at, RevisionNumber answeredWith)
    {
        RequestId = requestId;
        At = at;
        AnsweredWith = answeredWith;
    }
}


/// <summary>Chosen by the client and repeated with a retry. Identifies the request, never an entity.</summary>
public readonly record struct ProfileRevisionRequestId(Guid Value);
