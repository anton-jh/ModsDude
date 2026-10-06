using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Savegames;

/// <summary>What checking a savegame out against one game has to do first.</summary>
/// <remarks>
/// <see cref="SavegameHoldRules"/>, <see cref="SavegameCheckOut.CheckOutAsync"/> and the sync engine
/// refuse a check-out that skips either step. Those are the backstops nothing gets past; this is the
/// half that turns them into steps the check-out asks about before it writes anything.
/// </remarks>
/// <param name="ChecksInFirst">
/// The savegame with a profile already claiming this game's mod folder, which is checked in first.
/// Null where none is, and always for a savegame that follows no mod list, which claims no folder.
/// </param>
/// <param name="ActivatesFirst">
/// Whether the mod folder is not on the revision this savegame runs on, so its profile is activated
/// first.
/// </param>
/// <param name="PinnedRevision">
/// The revision a savegame in compatibility mode runs on, carried through so the activation can name
/// it. Null on latest, where the activation names the profile alone - it follows whatever the
/// profile says now, and a number there would be one the user has no reason to have heard of.
/// </param>
public sealed record SavegameRowOffer(
    Guid? ChecksInFirst,
    bool ActivatesFirst,
    int? PinnedRevision);


/// <summary>
/// What a savegame row's check-out has to do first against one game: check in the savegame holding
/// the mod folder, and activate the profile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure, for the same reason <see cref="SavegameHoldRules"/> is.</b> Reading the sync manifest,
/// listing the games and looking up names all happen around this, so the rule that decides
/// what a check-out has to do here and now is one function with one copy.
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
    /// The profile's head, which is what a savegame on latest runs on. Null where this member
    /// cannot see the profile at all, which is the same answer as following no mod list: nothing here
    /// can say what the folder ought to be on, so nothing is claimed about it.
    /// </param>
    /// <param name="pinnedRevision">
    /// What a savegame in compatibility mode runs on, from <see cref="SavegameRevisionRules.PinnedRevision"/>.
    /// Null on latest.
    /// </param>
    /// <param name="held">What the game is already holding.</param>
    /// <param name="appliedProfileId">
    /// Which profile the mod folder was last made to match, from the sync manifest, and
    /// <paramref name="appliedRevision"/> which revision of it. The manifest is the only thing that
    /// says what a folder is on, so a folder that has never been synced is one that is not ready.
    /// </param>
    /// <remarks>
    /// Only ever asked about a game that <em>is</em> connected here. A caller with none has nothing
    /// for this to decide.
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
        // The limit is one savegame claiming a mod folder, so it holds even where the folder is
        // already exactly right.
        var checksInFirst = profileId is null
            ? null
            : SavegameHoldRules.FindConflictingHold(held, savegameId)?.SavegameId;

        // Once that hold is gone, activating this savegame's own profile is always allowed, so
        // SavegameHoldRules.DecideApply is not asked: the only hold it could still find is this
        // savegame's own - checking out something already held here moves it between slots - and a
        // savegame never refuses the revision it itself pins.
        if (profileId is not Guid profile || headRevision is not int head)
        {
            return new SavegameRowOffer(checksInFirst, false, null);
        }

        // Head on latest, the pinned revision in compatibility mode.
        var required = pinnedRevision ?? head;

        return new SavegameRowOffer(
            checksInFirst,
            appliedProfileId != profile || appliedRevision != required,
            pinnedRevision);
    }

    /// <summary>
    /// Which list would be activated first, where <see cref="SavegameRowOffer.ActivatesFirst"/>.
    /// </summary>
    /// <remarks>
    /// The number only where the savegame pins one. On latest a savegame follows its profile, so naming a
    /// revision it happens to be at right now would be a number to memorise rather than a thing to know.
    /// </remarks>
    public static string DescribeActivation(string profileName, int? pinnedRevision)
        => pinnedRevision is int revision
            ? $"'{profileName}' rev {revision}"
            : $"'{profileName}'";
}
