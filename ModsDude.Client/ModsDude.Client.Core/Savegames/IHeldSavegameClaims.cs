namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What the savegame store says about the saves this machine holds: the one savegame fact the drift
/// check cannot find on this disk.
/// </summary>
public interface IHeldSavegameClaims
{
    /// <summary>The repos this machine holds a checked-out save in, ordered by id.</summary>
    IReadOnlyList<Guid> Repos();

    /// <summary>Each held save's head and claim as the store has them now, in a stable order.</summary>
    /// <remarks>Two captures are equal by <see cref="Enumerable.SequenceEqual{T}(IEnumerable{T}, IEnumerable{T})"/> when nothing the drift check reads changed.</remarks>
    IReadOnlyList<HeldSavegameClaim> Capture();
}


public sealed record HeldSavegameClaim(Guid RepoId, Guid SavegameId, int? Head, SavegameClaimSighting? Claim);
