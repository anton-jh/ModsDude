using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

public enum InstanceActivationKind
{
    /// <summary>The instance is already on this profile, so the folder is only being made to match again.</summary>
    Reapply,

    /// <summary>
    /// The instance is on a different profile, or on none. Applying re-syncs the folder, which means
    /// uninstalling whatever the previous profile put there.
    /// </summary>
    Activate
}

/// <summary>
/// What pairing a profile with an instance would actually do, so the control can be labelled for it
/// rather than for the screen it happens to sit on.
/// </summary>
public static class InstanceActivation
{
    public static InstanceActivationKind Describe(ActiveProfile? current, ActiveProfile target)
        => current == target ? InstanceActivationKind.Reapply : InstanceActivationKind.Activate;

    public static string Label(InstanceActivationKind kind)
        => kind is InstanceActivationKind.Reapply ? "Re-apply" : "Activate";
}
