using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Which of the two verbs a control runs.
/// </summary>
/// <remarks>
/// <b>Named for the verb rather than for the button.</b> Activating is intent - it sets which profile
/// the game follows, and runs the apply immediately after as one gesture; applying is work, and it
/// never records an intent. A game already on this profile has nothing to intend, so its control only
/// ever does the second half.
/// </remarks>
public enum ProfileActivationKind
{
    /// <summary>
    /// The game is already on this profile, so nothing is being decided - the folders are only being
    /// made to match again.
    /// </summary>
    Apply,

    /// <summary>
    /// The game is on a different profile, or on none. This records the intent and then applies,
    /// which means uninstalling whatever the previous profile put in the folders.
    /// </summary>
    Activate
}

/// <summary>
/// Which verb pairing a profile with a game would run, so the control can be labelled for it
/// rather than for the screen it happens to sit on.
/// </summary>
public static class ProfileActivation
{
    public static ProfileActivationKind Describe(ActiveProfile? current, ActiveProfile target)
        => current == target ? ProfileActivationKind.Apply : ProfileActivationKind.Activate;

    /// <param name="pinnedRevision">
    /// The revision a past savegame checked out here holds the mod folder to, from
    /// <see cref="Savegames.SavegameHoldRules.RequiredRevision"/>. Naming it is the whole change: the
    /// button normally applies the profile's latest, the apply table refuses that while a past savegame is
    /// held, and its only remaining job is repairing folder drift back to the revision that savegame runs
    /// on. Null - which is nearly always - leaves the label as it was.
    /// </param>
    public static string Label(ProfileActivationKind kind, int? pinnedRevision = null)
        => (kind, pinnedRevision) switch
        {
            (ProfileActivationKind.Apply, int revision) => $"Re-apply rev {revision}",
            (ProfileActivationKind.Apply, _) => "Re-apply",
            _ => "Activate"
        };
}
