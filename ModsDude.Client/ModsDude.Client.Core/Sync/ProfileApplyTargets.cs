using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Which instances a save on a profile re-applies to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, never asked.</b> An instance already carries its <see cref="ActiveProfile"/>, so the
/// targets of a re-apply are exactly the instances whose active profile is the one being saved.
/// There is nothing for the user to select.
/// </para>
/// <para>
/// Every awkward option - a checklist beside the button, a dropdown of instances, a pre-selected one
/// - comes from conflating two operations. <em>Re-apply</em> makes instances already on this profile
/// match it again, and its target is determined; <em>activate</em> moves an instance onto a different
/// profile, and its target is chosen, which is why activation belongs on the instance rather than on
/// a save button. A drifted instance falls out for free: its folder no longer matches its own active
/// profile, so it is already in this set.
/// </para>
/// </remarks>
public static class ProfileApplyTargets
{
    public static IReadOnlyList<LocalInstance> Derive(IEnumerable<LocalInstance> instances, ActiveProfile profile)
        => [.. instances.Where(x => x.ActiveProfile == profile)];

    /// <summary>
    /// What the save button says. One instance shows nothing at all - the word "instance" never
    /// appears, which is the common case for most games - and zero drops the apply entirely.
    /// </summary>
    public static string DescribeSaveAction(int targetCount) => targetCount switch
    {
        0 => "Save changes",
        1 => "Save and apply",
        var count => $"Save and apply to {count} instances"
    };
}
