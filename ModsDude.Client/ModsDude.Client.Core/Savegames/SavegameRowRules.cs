using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// What stands between a savegame and <em>Check out</em> against a particular game installation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AnotherSavegameIsHeld"/> is a sentence a disabled button carries instead of working;
/// <see cref="ModFolderElsewhere"/> is a question the button asks before it works.
/// <see cref="SavegameHoldRules"/>, <see cref="SavegameService.CheckOutAsync"/> and the sync engine
/// already refuse the first; those are the backstops nothing gets past, and this is the half that
/// makes the refusal arrive before the click rather than after it.
/// </para>
/// <para>
/// <b>Every value is about the pair - this savegame and this game.</b> "No game is connected here"
/// used to be one of them, carried through on a <c>hasGame</c> flag that was false at exactly one
/// call site; it is the absence of the thing this rule is about rather than something the rule
/// decides, so the caller that has no game says so itself and never asks.
/// </para>
/// </remarks>
public enum SavegameRowBlock
{
    /// <summary>Nothing is in the way.</summary>
    None,

    /// <summary>
    /// The mod folder is not on the revision this savegame runs on. Not a refusal: checking out, or
    /// taking a copy, asks to activate the profile first - see <see cref="SavegameRowOffer.ActivatesFirst"/>.
    /// </summary>
    ModFolderElsewhere,

    /// <summary>
    /// Another savegame with a profile already claims this game's mod folder. No activation clears
    /// it - checking that savegame in does.
    /// </summary>
    AnotherSavegameIsHeld
}


/// <summary>What checking a savegame out can do on one game, and why not where it cannot.</summary>
/// <param name="BlockingSavegameId">
/// The savegame already holding the mod folder, so a caller with the list can name it. Empty unless
/// the block is <see cref="SavegameRowBlock.AnotherSavegameIsHeld"/>.
/// </param>
/// <param name="PinnedRevision">
/// The revision a past savegame runs on, carried through so the activation can name it. Null for a
/// current savegame, whose activation names the profile alone - it follows whatever the profile says
/// now, and a number there would be one the user has no reason to have heard of.
/// </param>
public sealed record SavegameRowOffer(
    SavegameRowBlock CheckOut,
    Guid BlockingSavegameId,
    int? PinnedRevision)
{
    public bool CanCheckOut => CheckOut is SavegameRowBlock.None or SavegameRowBlock.ModFolderElsewhere;

    /// <summary>
    /// Whether the profile has to be activated before this savegame is played here, because the mod
    /// folder is not on the revision it runs on. Asked about rather than refused: the activation is
    /// the obvious next step, and a disabled button pointing at another one was a click the user
    /// always had to make anyway.
    /// </summary>
    public bool ActivatesFirst => CheckOut is SavegameRowBlock.ModFolderElsewhere;
}


/// <summary>
/// Whether a savegame row's check-out is on offer against one game, whether it activates the profile
/// first, and the words for where it is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure, for the same reason <see cref="SavegameHoldRules"/> is.</b> Reading the sync manifest,
/// listing the games and looking up names all happen around this, so the rule that decides
/// whether a savegame can be taken here and now is one function with one copy.
/// </para>
/// <para>
/// <b>The claim never syncs mods on its own</b> - see docs/10-savegame-profile-binding.md#activating-before-the-claim.
/// What this does is notice the folder is not right and say which activation would make it so, which
/// the check-out and the copy ask about, and run, before they write anything.
/// </para>
/// </remarks>
public static class SavegameRowRules
{
    /// <param name="profileId">The savegame's profile, or null where it follows no mod list.</param>
    /// <param name="headRevision">
    /// The profile's head, which is what a <em>current</em> savegame runs on. Null where this member
    /// cannot see the profile at all, which is the same answer as following no mod list: nothing here
    /// can say what the folder ought to be on, so nothing is claimed about it.
    /// </param>
    /// <param name="pinnedRevision">
    /// What a <em>past</em> savegame runs on, from <see cref="SavegameService.TargetRevisionOf"/>. Null
    /// for a current one.
    /// </param>
    /// <param name="held">What the game is already holding.</param>
    /// <param name="appliedProfileId">
    /// Which profile the mod folder was last made to match, from the sync manifest, and
    /// <paramref name="appliedRevision"/> which revision of it. The manifest is the only thing that
    /// says what a folder is on, so a folder that has never been synced is one that is not ready.
    /// </param>
    /// <remarks>
    /// Only ever asked about a game that <em>is</em> connected here. A caller with none has nothing
    /// for this to decide - see the remarks on <see cref="SavegameRowBlock"/>.
    /// </remarks>
    public static SavegameRowOffer Describe(
        Guid savegameId,
        Guid? profileId,
        int? headRevision,
        int? pinnedRevision,
        IReadOnlyList<SavegameCheckoutBinding> held,
        Guid? appliedProfileId,
        int? appliedRevision)
    {
        // Ahead of everything about the folder, because no activation clears it and because it holds
        // even where the folder is already exactly right: the limit is one savegame claiming a mod
        // folder, and the way past it is checking that one in.
        if (SavegameHoldRules.FindConflictingHold(held, savegameId) is SavegameCheckoutBinding blocking)
        {
            return new SavegameRowOffer(SavegameRowBlock.AnotherSavegameIsHeld, blocking.SavegameId, pinnedRevision);
        }

        // Past that check, activating this savegame's own profile is always allowed, so
        // SavegameHoldRules.DecideApply is not asked a second time: the only hold it could still find
        // is this savegame's own - checking out something already held here moves it between slots -
        // and a savegame never refuses the revision it itself pins.
        if (profileId is not Guid profile || headRevision is not int head)
        {
            return new SavegameRowOffer(SavegameRowBlock.None, Guid.Empty, null);
        }

        // Head for a current savegame, its own revision for a past one - the check-out table in
        // docs/10-savegame-profile-binding.md#which-revision-a-savegame-runs-on, read from the row.
        var required = pinnedRevision ?? head;

        return appliedProfileId == profile && appliedRevision == required
            ? new SavegameRowOffer(SavegameRowBlock.None, Guid.Empty, pinnedRevision)
            : new SavegameRowOffer(SavegameRowBlock.ModFolderElsewhere, Guid.Empty, pinnedRevision);
    }

    /// <summary>
    /// The disabled button's own explanation, which is the only place the refusal is ever said. Null
    /// where the button is not disabled.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Describe"/> the way <see cref="Sync.ProfileActivation.Label"/> is
    /// separate from its <c>Describe</c>: the rule works on ids, and the names a sentence needs belong
    /// to whoever loaded the list.
    /// </remarks>
    /// <param name="blockingSavegameName">
    /// What the savegame holding the folder is called, where the caller could find out. A row that
    /// could not is still refused, and says so without the name.
    /// </param>
    public static string? Explain(SavegameRowBlock block, string? blockingSavegameName)
    {
        return block switch
        {
            SavegameRowBlock.AnotherSavegameIsHeld => blockingSavegameName is { Length: > 0 } name
                ? $"'{name}' is checked out here"
                : "Another savegame is checked out here",

            _ => null
        };
    }

    /// <summary>
    /// Which list would be activated first, where <see cref="SavegameRowOffer.ActivatesFirst"/>.
    /// </summary>
    /// <remarks>
    /// The number only where the savegame pins one. A current savegame follows its profile, so naming a
    /// revision it happens to be at right now would be a number to memorise rather than a thing to know.
    /// </remarks>
    public static string DescribeActivation(string profileName, int? pinnedRevision)
        => pinnedRevision is int revision
            ? $"'{profileName}' rev {revision}"
            : $"'{profileName}'";
}
