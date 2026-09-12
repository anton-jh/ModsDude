using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

public enum InstanceActivationKind
{
    /// <summary>The game is already on this profile, so the folder is only being made to match again.</summary>
    Reapply,

    /// <summary>
    /// The game is on a different profile, or on none. Applying re-syncs the folder, which means
    /// uninstalling whatever the previous profile put there.
    /// </summary>
    Activate
}

/// <summary>
/// What pairing a profile with a game would actually do, so the control can be labelled for it
/// rather than for the screen it happens to sit on.
/// </summary>
public static class InstanceActivation
{
    public static InstanceActivationKind Describe(ActiveProfile? current, ActiveProfile target)
        => current == target ? InstanceActivationKind.Reapply : InstanceActivationKind.Activate;

    /// <param name="pinnedRevision">
    /// The revision a past savegame checked out here holds the mod folder to, from
    /// <see cref="Savegames.SavegameHoldRules.RequiredRevision"/>. Naming it is the whole change: the
    /// button normally applies the profile's latest, the apply table refuses that while a past savegame is
    /// held, and its only remaining job is repairing folder drift back to the revision that savegame runs
    /// on. Null - which is nearly always - leaves the label as it was.
    /// </param>
    public static string Label(InstanceActivationKind kind, int? pinnedRevision = null)
        => (kind, pinnedRevision) switch
        {
            (InstanceActivationKind.Reapply, int revision) => $"Re-apply rev {revision}",
            (InstanceActivationKind.Reapply, _) => "Re-apply",
            _ => "Activate"
        };
}
