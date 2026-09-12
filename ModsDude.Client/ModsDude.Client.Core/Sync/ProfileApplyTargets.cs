using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Which games a save on a profile re-applies to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, never asked.</b> A game already carries its <see cref="ActiveProfile"/>, so the
/// targets of a re-apply are exactly the games whose active profile is the one being saved.
/// There is nothing for the user to select.
/// </para>
/// <para>
/// Every awkward option - a checklist beside the button, a dropdown of games, a pre-selected one
/// - comes from conflating two operations. <em>Re-apply</em> makes games already on this profile
/// match it again, and its target is determined; <em>activate</em> moves a game onto a different
/// profile, and its target is chosen, which is why activation belongs on the game rather than on
/// a save button. A drifted game falls out for free: its folder no longer matches its own active
/// profile, so it is already in this set.
/// </para>
/// </remarks>
public static class ProfileApplyTargets
{
    public static IReadOnlyList<Game> Derive(IEnumerable<Game> games, ActiveProfile profile)
        => [.. games.Where(x => x.ActiveProfile == profile)];

    /// <summary>
    /// What the save button says. One game is never named - which is the common case for most
    /// games - and zero drops the apply entirely.
    /// </summary>
    public static string DescribeSaveAction(int targetCount) => targetCount switch
    {
        0 => "Save changes",
        1 => "Save and apply",
        var count => $"Save and apply to {count} games"
    };
}
