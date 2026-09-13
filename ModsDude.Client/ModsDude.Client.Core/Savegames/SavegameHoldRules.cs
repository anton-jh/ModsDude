using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// Why the savegames a game is holding refuse a profile being applied to its mod folder.
/// </summary>
/// <remarks>
/// Both refusals are about the same thing: <b>one mod folder can only be on one revision</b>, and a
/// held savegame has already said which. Neither is a warning - a savegame quietly taken off the mod list
/// it runs on is the state this whole design exists to prevent, and there is no wording that makes a
/// button doing it safe.
/// </remarks>
public enum SavegameApplyRefusal
{
    /// <summary>Nothing held here constrains the folder.</summary>
    None,

    /// <summary>
    /// A savegame following a <em>different</em> profile is checked out here. This is the
    /// active-profile switch, refused: applying another list under a held savegame is the case that
    /// corrupts saves.
    /// </summary>
    AnotherProfileIsHeld,

    /// <summary>
    /// A past savegame is checked out here, and it runs on one revision only. Re-applying that
    /// revision is allowed - repairing folder drift is exactly what it is for - and anything else,
    /// head included, would move a savegame whose revision does not move.
    /// </summary>
    PastSavegameIsHeld
}


/// <summary>
/// Whether an apply may go ahead, and which held savegame says otherwise.
/// </summary>
/// <param name="SavegameId">The savegame doing the refusing, so a caller can name it. Empty when allowed.</param>
/// <param name="ProfileId">The profile that savegame follows.</param>
/// <param name="Revision">The revision it pins the folder to, where it pins one.</param>
public sealed record SavegameApplyDecision(
    SavegameApplyRefusal Refusal,
    Guid SavegameId,
    Guid? ProfileId,
    int? Revision)
{
    public static SavegameApplyDecision Allowed { get; } = new(SavegameApplyRefusal.None, Guid.Empty, null, null);

    public bool IsAllowed => Refusal is SavegameApplyRefusal.None;
}


/// <summary>
/// What the savegames a game is holding demand of its mod folder: which revision it has to be
/// on, whether a given apply may run, and whether another savegame may be checked out beside them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure, for the same reason <see cref="SavegameSlotStates"/> is.</b> Reading the bindings out of
/// local state, asking the server what is current and running the sync all happen around these,
/// never inside them - so the rules that decide whether a savegame gets taken off its mod list are three
/// short functions with one copy each.
/// </para>
/// <para>
/// <b>Every rule here ignores savegames with no profile.</b> Such a savegame claims no mod list, so
/// nothing about the folder is its to constrain and nothing about the folder can be wrong for it.
/// That is also why no adapter-capability check appears anywhere below: in a repo whose adapter has
/// no mod support, no savegame has a profile, so nothing is ever constrained.
/// </para>
/// </remarks>
public static class SavegameHoldRules
{
    /// <summary>
    /// Which revision this game's mod folder has to be on for one profile, or null where nothing
    /// held here pins it and the profile's head is the answer.
    /// </summary>
    /// <remarks>
    /// Read by the apply, which installs it in place of head, and by the drift check, which compares
    /// against it in place of head. Nothing is suppressed by the second: a game holding a past
    /// savegame is behind head by construction, and comparing it against head instead would report
    /// drift permanently while offering a re-apply the apply table refuses.
    /// </remarks>
    public static int? RequiredRevision(IReadOnlyList<SavegameCheckoutBinding> held, Guid profileId)
    {
        foreach (var binding in held)
        {
            if (binding.ProfileId == profileId && binding.TargetRevision is int pinned)
            {
                return pinned;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a profile may be applied to this game, per the apply table in
    /// docs/10-savegame-profile-binding.md#applying-to-a-game-that-holds-a-savegame.
    /// </summary>
    /// <param name="revision">
    /// The revision about to be installed, or null where the caller has not chosen one and will take
    /// whatever <see cref="RequiredRevision"/> says. Null therefore never trips the past-savegame
    /// refusal - it cannot be the wrong revision when it is not a revision.
    /// </param>
    public static SavegameApplyDecision DecideApply(
        IReadOnlyList<SavegameCheckoutBinding> held,
        Guid profileId,
        int? revision)
    {
        foreach (var binding in held)
        {
            if (binding.ProfileId is not Guid heldProfile)
            {
                continue;
            }

            // Checked before the revisions, because two profiles' revision numbers are not comparable
            // at all: revision 6 of 'Season 4' and revision 6 of 'Vanilla' are different mod lists
            // that happen to share an integer.
            if (heldProfile != profileId)
            {
                return new SavegameApplyDecision(
                    SavegameApplyRefusal.AnotherProfileIsHeld, binding.SavegameId, heldProfile, binding.TargetRevision);
            }

            if (binding.TargetRevision is int pinned && revision is int asked && asked != pinned)
            {
                return new SavegameApplyDecision(
                    SavegameApplyRefusal.PastSavegameIsHeld, binding.SavegameId, heldProfile, pinned);
            }
        }

        return SavegameApplyDecision.Allowed;
    }

    /// <summary>
    /// The savegame already holding this game's mod folder, which is what stops a second one
    /// being checked out into it. Null where nothing does.
    /// </summary>
    /// <remarks>
    /// <b>The limit counts savegames with a profile, not savegames.</b> It is not about savegames at
    /// all - it is about the folder, which can only be on one revision - so any number of profile-less
    /// ones may be held alongside, bounded only by the slots the adapter offers and by
    /// <see cref="SavegameBindingStore"/>'s existing one-binding-per-slot rule.
    /// </remarks>
    /// <param name="savegameId">
    /// The savegame about to be taken. A binding for that same savegame is not a conflict: checking
    /// out something this game already holds moves it to another slot rather than making it two.
    /// </param>
    public static SavegameCheckoutBinding? FindConflictingHold(
        IReadOnlyList<SavegameCheckoutBinding> held,
        Guid savegameId)
    {
        foreach (var binding in held)
        {
            if (binding.ProfileId is not null && binding.SavegameId != savegameId)
            {
                return binding;
            }
        }

        return null;
    }
}
