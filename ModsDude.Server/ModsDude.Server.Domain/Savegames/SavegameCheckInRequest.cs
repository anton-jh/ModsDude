using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// The latest check-in one person made on one savegame, and what it was answered with, so a repeat
/// of it is given the same answer instead of being refused as stale.
/// </summary>
/// <remarks>
/// One per person per savegame: a client only ever repeats its latest check-in, so a newer one
/// replaces the row rather than adding to it.
/// </remarks>
public class SavegameCheckInRequest
{
    // ef
    private SavegameCheckInRequest() { }

    public SavegameCheckInRequest(
        RepoId repoId,
        SavegameId savegameId,
        UserId userId,
        SavegameCheckInRequestId requestId,
        DateTime at,
        SavegameSnapshotNumber answeredWith,
        bool callerHoldsClaim,
        SavegameCheckoutId? takenFrom)
    {
        RepoId = repoId;
        SavegameId = savegameId;
        UserId = userId;
        Replace(requestId, at, answeredWith, callerHoldsClaim, takenFrom);
    }


    public RepoId RepoId { get; private set; }
    public SavegameId SavegameId { get; private set; }
    public UserId UserId { get; private set; }

    public SavegameCheckInRequestId RequestId { get; private set; }
    public DateTime At { get; private set; }

    /// <summary>The snapshot the check-in was answered with: the one it minted, or the head where nothing changed.</summary>
    public SavegameSnapshotNumber AnsweredWith { get; private set; }

    public bool CallerHoldsClaim { get; private set; }

    /// <summary>The claim the check-in took from somebody else, or null where it took none.</summary>
    public SavegameCheckoutId? TakenFrom { get; private set; }


    public bool Answers(SavegameCheckInRequestId requestId) => RequestId == requestId;

    /// <summary>Records a newer check-in by the same person, which is the only one they can still repeat.</summary>
    public void Replace(
        SavegameCheckInRequestId requestId,
        DateTime at,
        SavegameSnapshotNumber answeredWith,
        bool callerHoldsClaim,
        SavegameCheckoutId? takenFrom)
    {
        RequestId = requestId;
        At = at;
        AnsweredWith = answeredWith;
        CallerHoldsClaim = callerHoldsClaim;
        TakenFrom = takenFrom;
    }
}


/// <summary>Chosen by the client and repeated with a retry. Identifies the request, never an entity.</summary>
public readonly record struct SavegameCheckInRequestId(Guid Value);
