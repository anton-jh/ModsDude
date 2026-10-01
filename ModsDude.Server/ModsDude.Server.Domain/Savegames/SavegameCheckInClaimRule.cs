using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Savegames;

/// <summary>
/// What a check-in does to the savegame's open claim.
/// </summary>
public static class SavegameCheckInClaimRule
{
    /// <param name="open">The savegame's open claim, or null where nobody holds it.</param>
    /// <param name="keepPlaying">Whether the caller wants to carry on holding the save.</param>
    /// <param name="takeOver">
    /// Whether the caller agreed to take the claim from somebody else. Only read with
    /// <paramref name="keepPlaying"/>: a check-in that hands the save back takes nothing from anybody.
    /// </param>
    public static SavegameCheckInClaim Decide(SavegameCheckout? open, UserId caller, bool keepPlaying, bool takeOver)
    {
        if (open is not null && open.IsOpen is false)
        {
            throw new DomainValidationException($"Checkout '{open.Id.Value}' has already ended, so it is not the open claim.");
        }

        if (open is not null && open.UserId == caller)
        {
            return keepPlaying ? SavegameCheckInClaim.KeepsCallers : SavegameCheckInClaim.EndsCallers;
        }

        if (keepPlaying is false)
        {
            // A forced check-in by somebody who never had the save ends nobody's claim: their claim
            // stands, and the new head is what tells them they were overtaken.
            return SavegameCheckInClaim.LeavesAlone;
        }

        if (open is null)
        {
            return SavegameCheckInClaim.OpensCallers;
        }

        return takeOver ? SavegameCheckInClaim.TakesOver : SavegameCheckInClaim.RefusedHeldByOther;
    }
}


public enum SavegameCheckInClaim
{
    /// <summary>The caller held it and handed it back.</summary>
    EndsCallers,

    /// <summary>The caller held it and carries on holding it.</summary>
    KeepsCallers,

    /// <summary>The caller does not hold it and is handing the save back: whoever holds it, if anybody, keeps it.</summary>
    LeavesAlone,

    /// <summary>Nobody held it, and the caller carries on with a new claim.</summary>
    OpensCallers,

    /// <summary>Somebody else held it, and the caller agreed to take it from them.</summary>
    TakesOver,

    /// <summary>Somebody else holds it, and the caller has not agreed to take it from them.</summary>
    RefusedHeldByOther
}


public static class SavegameCheckInClaimExtensions
{
    /// <summary>Whether the caller holds the claim once the check-in is done.</summary>
    public static bool CallerHolds(this SavegameCheckInClaim claim) => claim is
        SavegameCheckInClaim.KeepsCallers or SavegameCheckInClaim.OpensCallers or SavegameCheckInClaim.TakesOver;

    /// <summary>
    /// Whether a snapshot minted by this check-in was checked in against the open claim. A claim opened
    /// or taken here starts here, like the one a publish opens.
    /// </summary>
    public static bool RecordsAgainstOpenClaim(this SavegameCheckInClaim claim) => claim is
        SavegameCheckInClaim.EndsCallers or SavegameCheckInClaim.KeepsCallers;
}
