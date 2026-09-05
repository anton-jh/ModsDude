using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What one slot is, from the point of view of somebody about to write a savegame into it.
/// </summary>
/// <remarks>
/// Four states rather than the two the filesystem offers, because "occupied" is three different
/// problems with three different remedies: bytes ModsDude wrote and has published, bytes ModsDude
/// wrote and somebody has since played, and bytes ModsDude has never seen. Only the middle one is
/// unrecoverable, and only the last one belongs to a person who never asked ModsDude for anything.
/// </remarks>
public enum SavegameSlotAvailability
{
    /// <summary>Nothing there. Write it, no confirmation - there is nothing to lose.</summary>
    Free,

    /// <summary>
    /// A savegame is checked out here and the slot still holds exactly what was written into it.
    /// Overwriting costs nothing anybody cannot get back off the server, so this is a confirmation
    /// rather than a refusal.
    /// </summary>
    HeldClean,

    /// <summary>
    /// A savegame is checked out here and the contents have moved since. This is somebody's evening,
    /// and it exists nowhere else - see <see cref="SavegameSlotWriteDecision.Refused"/>.
    /// </summary>
    HeldWithUnpublishedPlay,

    /// <summary>
    /// Occupied by something this machine never checked out - somebody's own save, never published.
    /// ModsDude has no claim on it and no copy of it, so displacing it is a confirmation naming what
    /// the game calls the save, and the folder goes to the Recycle Bin rather than being deleted.
    /// </summary>
    Unrecognised
}


/// <summary>
/// What a caller is allowed to do with a slot it wants to write into.
/// </summary>
/// <remarks>
/// Separate from <see cref="SavegameSlotAvailability"/> because the two answer different questions:
/// availability is what the slot <em>is</em>, and this is what the app <em>does</em> about it. Two
/// states map to <see cref="NeedsConfirmation"/> for entirely different reasons, and a caller
/// prompting must still read the availability to word the prompt.
/// </remarks>
public enum SavegameSlotWriteDecision
{
    /// <summary>Write it. The ordinary night stays one click.</summary>
    Allowed,

    /// <summary>
    /// Ask first. Something is there that the user may not expect to lose, but nothing that cannot
    /// be got back - either off the server, or out of the Recycle Bin.
    /// </summary>
    NeedsConfirmation,

    /// <summary>
    /// Do not offer it at all. A confirmation here is a button that destroys unpublished play, and
    /// the answer is to check that savegame in first - which is an action, not a warning.
    /// </summary>
    Refused
}


/// <summary>
/// Whether a slot can be written into, decided from the slot, the binding this machine holds for it,
/// and what is in it now.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure, and deliberately so.</b> The rule that decides whether somebody's unchecked-in farm gets
/// overwritten is one <c>if</c> chain with no filesystem, no server and no clock in it, so it can be
/// exercised exhaustively by tests and so there is exactly one copy of it. Hashing the slot, reading
/// the binding out of local state and putting the folder in the Recycle Bin all happen around this,
/// never inside it.
/// </para>
/// <para>
/// <b>It errs toward "there may be play here" wherever it is unsure.</b> A false positive costs one
/// needless prompt; a false negative costs somebody their evening. That asymmetry is the whole
/// design, and it is why an uncomputed hash reads as
/// <see cref="SavegameSlotAvailability.HeldWithUnpublishedPlay"/> rather than as clean.
/// </para>
/// </remarks>
public static class SavegameSlotStates
{
    /// <param name="slot">
    /// The slot as the adapter reports it. Only <see cref="SavegameSlot.IsOccupied"/> is consulted;
    /// the display name is for the prompt, not for the decision.
    /// </param>
    /// <param name="binding">
    /// What this machine records as being checked out into this slot, or null for none. A binding
    /// naming a <em>different</em> slot says nothing about this one and is treated as none - see the
    /// guard below.
    /// </param>
    /// <param name="currentContentHash">
    /// The slot's contents hashed now, or null where the caller has not hashed it. Null is
    /// <b>not</b> "unchanged": hashing a savegame folder costs real time, so a caller is allowed to
    /// skip it, and what it gets back is the cautious answer rather than the cheap one.
    /// </param>
    public static SavegameSlotAvailability Classify(
        SavegameSlot slot,
        SavegameCheckoutBinding? binding,
        string? currentContentHash)
    {
        // A binding for another slot is a caller mistake, and the one thing that must not happen is
        // using its hash to declare this slot clean. Reduced to "no binding", which lands on the
        // cautious side of every remaining branch.
        if (binding is not SavegameCheckoutBinding held || SlotIdsMatch(held.SlotId, slot.Id) is false)
        {
            // Occupied with nothing recording who put it there is somebody's own save, never
            // published. ModsDude has no copy of it, so it is never quietly replaced.
            return slot.IsOccupied
                ? SavegameSlotAvailability.Unrecognised
                : SavegameSlotAvailability.Free;
        }

        // A binding over an empty slot: the user deleted the folder from inside the game, or moved
        // it, and the binding outlived what it described. Neither Unrecognised (there is nothing
        // there to be unrecognised) nor an error - there is nothing to lose, so it is Free.
        //
        // The stale binding itself is the caller's to clean up: this classifier is pure and does not
        // write, and check-in is the code that knows whether the savegame is still worth holding.
        if (slot.IsOccupied is false)
        {
            return SavegameSlotAvailability.Free;
        }

        // Reused rather than a string comparison of its own, so that every recorded hash in the
        // client agrees about hex casing. A hash the caller did not compute never matches, which is
        // what makes null read as "there may be play here".
        return ModContentHasher.Matches(currentContentHash, held.ContentHash)
            ? SavegameSlotAvailability.HeldClean
            : SavegameSlotAvailability.HeldWithUnpublishedPlay;
    }


    /// <summary>What the app may do about writing into a slot in this state.</summary>
    public static SavegameSlotWriteDecision DecideWrite(SavegameSlotAvailability availability) => availability switch
    {
        SavegameSlotAvailability.Free => SavegameSlotWriteDecision.Allowed,

        // Refused, not warned. The slot holds play that exists nowhere else, and the remedy is to
        // check that savegame in first - offered as a single action, per docs/PLAN.md#slot-safety.
        SavegameSlotAvailability.HeldWithUnpublishedPlay => SavegameSlotWriteDecision.Refused,

        // The bytes here are on the server already, so this asks only because a slot silently
        // changing under somebody is unpleasant - not because anything is at risk.
        SavegameSlotAvailability.HeldClean => SavegameSlotWriteDecision.NeedsConfirmation,

        // Asks naming what the *game* calls the save, never the folder number, and recycles rather
        // than deletes. Same rule and the same reasoning as an unrecognised mod file.
        SavegameSlotAvailability.Unrecognised => SavegameSlotWriteDecision.NeedsConfirmation,

        _ => SavegameSlotWriteDecision.NeedsConfirmation
    };

    /// <inheritdoc cref="DecideWrite(SavegameSlotAvailability)"/>
    public static SavegameSlotWriteDecision DecideWrite(
        SavegameSlot slot,
        SavegameCheckoutBinding? binding,
        string? currentContentHash)
        => DecideWrite(Classify(slot, binding, currentContentHash));

    /// <summary>
    /// Whether writing here has to be confirmed first. False for a refusal too - a refused write is
    /// not a confirmation the user is able to give, so a caller that only asked this question would
    /// have written straight over unpublished play.
    /// </summary>
    public static bool RequiresConfirmation(SavegameSlotAvailability availability)
        => DecideWrite(availability) is SavegameSlotWriteDecision.NeedsConfirmation;

    /// <summary>
    /// Whether writing here is refused outright, with no prompt on offer. Callers building a picker
    /// use this to disable the row and show the check-in action instead.
    /// </summary>
    public static bool IsRefused(SavegameSlotAvailability availability)
        => DecideWrite(availability) is SavegameSlotWriteDecision.Refused;


    /// <summary>
    /// Whether two adapter slot ids address the same place. Case-insensitive: these are folder names
    /// and save names on Windows, where two spellings are one place. Treating them as two slots would
    /// let a write land on top of a save the binding was protecting, which is the failure this whole
    /// file exists to prevent; treating them as one at worst refuses a write that was fine.
    /// </summary>
    private static bool SlotIdsMatch(string left, SavegameSlotId right)
        => string.Equals(left, right.Value, StringComparison.OrdinalIgnoreCase);
}
