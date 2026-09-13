using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Which game a save on a profile re-applies to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, never asked - and now a lookup rather than a search.</b> A profile belongs to a repo,
/// a repo is about one game, and a machine configures that game <em>once</em>: so a profile maps to
/// exactly one <see cref="Game"/>, which either follows it or does not. What used to be a set is a
/// yes or a no.
/// </para>
/// <para>
/// <b>Once configured is not once installed.</b> A game reaching three targets is usually three
/// installations - a dedicated server, an MP client and a singleplayer copy are separate downloads
/// in separate folders - and this system has never modelled installations at all. What there is one
/// of is the <em>policy holder</em>: one active profile, one savegame hold, for however many folders
/// the adapter reaches. Reading this as "one copy of the game on disk" is the conflation Phase 10
/// exists to remove, wearing a different word.
/// </para>
/// <para>
/// Every awkward option - a checklist beside the button, a dropdown of games, a pre-selected one -
/// came from conflating two operations. <em>Apply</em> makes the game already on this profile match
/// it again, and has nothing to choose; <em>activate</em> moves a game onto a different profile, and
/// is a decision, which is why it belongs on the profile page rather than on a save button. A
/// drifted game falls out for free: its folders no longer match its own active profile, so it is
/// still the answer here.
/// </para>
/// </remarks>
public static class ProfileApplyTarget
{
    /// <summary>
    /// The game a save on this profile applies to, or null where none follows it.
    /// </summary>
    /// <param name="scope">
    /// Which game the profile's repo is about. The answer is that game or nothing, so this is a
    /// lookup by identity rather than a scan for candidates.
    /// </param>
    public static Game? Find(IEnumerable<Game> games, GameIdentity scope, ActiveProfile profile)
    {
        foreach (var game in games)
        {
            if (game.Identity == scope && game.ActiveProfile == profile)
            {
                return game;
            }
        }

        return null;
    }

    /// <summary>
    /// What the save button says.
    /// </summary>
    /// <remarks>
    /// The game is never named: there is one, the user is looking at its repo, and "Save and apply to
    /// Farming Simulator 25" is a sentence that reads as though there were a choice. Nothing to apply
    /// to is the onboarding case - a profile no game follows yet - and the button says so rather than
    /// promising an apply that would reach nowhere; the offer to activate comes after the save.
    /// </remarks>
    public static string DescribeSaveAction(bool hasTarget) => hasTarget ? "Save and apply" : "Save changes";
}
