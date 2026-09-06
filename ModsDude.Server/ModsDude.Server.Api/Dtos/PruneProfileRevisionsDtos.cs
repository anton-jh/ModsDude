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
/// The savegame versions played on this revision, for <see cref="BlockedRevisionReason.PlayedOn"/>.
/// Empty otherwise. They are what the user has to remove first, so they are named rather than
/// counted.
/// </param>
public record BlockedRevisionDto(
    int Revision,
    BlockedRevisionReason Reason,
    IEnumerable<SavegameVersionRefDto> Savegames);

public enum BlockedRevisionReason
{
    /// <summary>
    /// What the profile currently pins. Emptying a profile is done by editing it, not by deleting
    /// what it says, so this one can never be pruned.
    /// </summary>
    IsHead,

    /// <summary>
    /// A savegame version records having been played on it, and a save whose mod list is gone is not
    /// restorable - which is the only thing that made keeping it worth anything.
    /// </summary>
    PlayedOn
}

/// <summary>One savegame version, named the way somebody would say it out loud.</summary>
public record SavegameVersionRefDto(Guid SavegameId, string SavegameName, int Number);
