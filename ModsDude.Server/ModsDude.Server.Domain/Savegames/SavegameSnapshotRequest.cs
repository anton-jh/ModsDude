using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// The latest request one person made that writes a snapshot of one savegame - a check-in or a
/// restore - and what it was answered with, so a repeat of it is given the same answer instead of
/// being refused as stale or minting a second snapshot.
/// </summary>
/// <remarks>
/// One per person per savegame: a client only ever repeats its latest request, so a newer one
/// replaces the row rather than adding to it.
/// </remarks>
public class SavegameSnapshotRequest
{
    // ef
    private SavegameSnapshotRequest() { }

    public SavegameSnapshotRequest(
        RepoId repoId,
        SavegameId savegameId,
        UserId userId,
        SavegameSnapshotRequestId requestId,
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

    public SavegameSnapshotRequestId RequestId { get; private set; }
    public DateTime At { get; private set; }

    /// <summary>The snapshot the request was answered with: the one it minted, or the head where nothing changed.</summary>
    public SavegameSnapshotNumber AnsweredWith { get; private set; }

    public bool CallerHoldsClaim { get; private set; }

    /// <summary>The claim a check-in took from somebody else, or null where it took none.</summary>
    public SavegameCheckoutId? TakenFrom { get; private set; }


    public bool Answers(SavegameSnapshotRequestId requestId) => RequestId == requestId;

    /// <summary>Records a newer request by the same person, which is the only one they can still repeat.</summary>
    public void Replace(
        SavegameSnapshotRequestId requestId,
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
public readonly record struct SavegameSnapshotRequestId(Guid Value);
