using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// The publish that created a savegame, and what it was answered with, so a repeat of it is given
/// the same answer instead of being refused because the name is now taken.
/// </summary>
public class SavegamePublishRequest
{
    // ef
    private SavegamePublishRequest() { }

    public SavegamePublishRequest(
        RepoId repoId,
        SavegameId savegameId,
        UserId userId,
        SavegamePublishRequestId requestId,
        DateTime at,
        bool callerHoldsClaim)
    {
        RepoId = repoId;
        SavegameId = savegameId;
        UserId = userId;
        RequestId = requestId;
        At = at;
        CallerHoldsClaim = callerHoldsClaim;
    }


    public RepoId RepoId { get; private set; }
    public SavegameId SavegameId { get; private set; }
    public UserId UserId { get; private set; }

    public SavegamePublishRequestId RequestId { get; private set; }
    public DateTime At { get; private set; }

    public bool CallerHoldsClaim { get; private set; }
}


/// <summary>Chosen by the client and repeated with a retry. Identifies the request, never an entity.</summary>
public readonly record struct SavegamePublishRequestId(Guid Value);
