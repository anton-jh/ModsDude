using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// What putting one version of a mod into a profile would do, given what the profile already holds of it.
/// </summary>
public enum ProfileVersionMove
{
    /// <summary>A mod the profile has never held.</summary>
    Add,

    /// <summary>A later version of one it holds.</summary>
    Update,

    /// <summary>
    /// Any other version of one it holds: an earlier one, or one the ordering will not place against the
    /// pin. Only a later version is an update, and only where the order says so.
    /// </summary>
    Move,

    /// <summary>
    /// A move the profile would make were the pin not held. A lock is not a question a selection gets to
    /// answer, so it is counted apart from the moves that will happen.
    /// </summary>
    Locked,

    /// <summary>It is the version the profile is on already.</summary>
    Nothing
}


public static class ProfileVersionMoves
{
    /// <summary>
    /// Which of the moves this is.
    /// </summary>
    /// <param name="chosen">The version being put in.</param>
    /// <param name="held">
    /// The version the profile pins of this mod, or null where it pins none.
    /// </param>
    /// <param name="heldIsLocked">Whether that pin is held in place, by the adapter or by the profile.</param>
    /// <param name="set">
    /// The mod's ordering. Null answers <see cref="ProfileVersionMove.Move"/> for anything that is not
    /// the same version: with no order to read, nothing can honestly be called later or earlier.
    /// </param>
    /// <remarks>
    /// <b>Later and earlier are only said where the order says them.</b> A pair the comparer abstained
    /// on is neither - the same rule the update planner applies, and for the same reason: calling it an
    /// update is how a possible downgrade gets offered as one.
    /// </remarks>
    public static ProfileVersionMove Classify(
        ModVersionKey chosen,
        ModVersionKey? held,
        bool heldIsLocked,
        ModVersionSet? set)
    {
        if (held is not ModVersionKey pinned)
        {
            return ProfileVersionMove.Add;
        }

        if (pinned == chosen)
        {
            return ProfileVersionMove.Nothing;
        }

        if (heldIsLocked)
        {
            return ProfileVersionMove.Locked;
        }

        if (set?.IsAfter(chosen, pinned) is true)
        {
            return ProfileVersionMove.Update;
        }

        return ProfileVersionMove.Move;
    }
}
