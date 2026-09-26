namespace ModsDude.Server.Api.Dtos;

/// <param name="Revisions">
/// Which revisions to remove. Repeats and revisions that are already gone are ignored rather than
/// refused - the caller wanted them absent, and they are.
/// </param>
public record PruneProfileRevisionsRequest(IEnumerable<int> Revisions);

/// <param name="Deleted">How many rows went, which is what the page reports.</param>
/// <param name="Blocked">
/// The revisions that stayed, and why. Deleting what it can and naming what it cannot is what stops
/// pruning a hundred revisions turning into an exercise in bisection.
/// </param>
public record PruneProfileRevisionsResponse(
    int Deleted,
    IEnumerable<BlockedRevisionDto> Blocked);

/// <param name="Savegames">
/// The savegame snapshots played on this revision. Empty for the head, which is refused before
/// anything else is asked. They are what the user has to remove first, so they are named rather
/// than counted.
/// </param>
/// <param name="Checkouts">
/// The open claims that may be playing on this revision, for <see cref="BlockedRevisionReason.CheckedOut"/>.
/// Empty otherwise. Named with who holds them, because the next step is asking that person.
/// </param>
public record BlockedRevisionDto(
    int Revision,
    BlockedRevisionReason Reason,
    IEnumerable<SavegameSnapshotRefDto> Savegames,
    IEnumerable<CheckedOutSavegameRefDto> Checkouts);

public enum BlockedRevisionReason
{
    /// <summary>
    /// What the profile currently pins. Emptying a profile is done by editing it, not by deleting
    /// what it says, so this one can never be pruned.
    /// </summary>
    IsHead,

    /// <summary>
    /// A savegame snapshot records having been played on it, and a save whose mod list is gone is not
    /// restorable - which is the only thing that made keeping it worth anything.
    /// </summary>
    PlayedOn,

    /// <summary>
    /// A savegame of this profile is checked out, and its play - which no snapshot names until it is
    /// checked in - may be on this revision. Deleting it would leave that check-in naming a revision
    /// that no longer exists, which is refused. Reported ahead of <see cref="PlayedOn"/>: it clears by
    /// itself, and deleting snapshots would not free the revision while it stands.
    /// </summary>
    CheckedOut
}

/// <summary>One savegame snapshot, named the way somebody would say it out loud.</summary>
public record SavegameSnapshotRefDto(Guid SavegameId, string SavegameName, int Number);

/// <summary>One open claim on a savegame, named by the save and by who holds it.</summary>
public record CheckedOutSavegameRefDto(Guid SavegameId, string SavegameName, UserDto HeldBy, DateTime TakenAt);
