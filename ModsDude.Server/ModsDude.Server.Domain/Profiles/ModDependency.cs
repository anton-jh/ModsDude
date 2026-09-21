using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// One mod, pinned by one revision at one version.
/// </summary>
/// <remarks>
/// Set once and never changed: it belongs to a <see cref="ProfileRevision"/>, and a revision is a
/// snapshot. What used to move a pin - upgrading it, changing its version - is now a new revision
/// carrying a different set, which is what makes "what did we run last week" a question with an
/// answer.
/// </remarks>
public class ModDependency
{
    public required ModVersion ModVersion { get; init; }
    public required bool Locked { get; init; }

    /// <summary>
    /// When this mod arrived in the profile at this version: the instant of the revision that first
    /// pinned it, carried forward unchanged for as long as every revision after keeps pinning that
    /// same version. A new version, or a mod taken out and put back, starts again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What would change the game, not what changed the record.</b> The lock is deliberately not
    /// part of it: toggling one moves no file, so the date stays what it was and the list can be
    /// sorted by "what did somebody last do that I would notice in game".
    /// </para>
    /// <para>
    /// Stamped by <see cref="ProfileRevision"/> as it is constructed rather than passed in, because it
    /// is a fact about the pair of revisions and only the revision holds both sides of it. Set once,
    /// like everything else here.
    /// </para>
    /// </remarks>
    public DateTime Added { get; internal set; }

    /// <summary>
    /// A mod is held in a profile's mod list when either the profile pins it or the adapter marked
    /// the mod itself version-sensitive. Keeping the disjunction here puts the rule in one place.
    /// </summary>
    public bool IsEffectivelyLocked => Locked || ModVersion.Locked;


    public ProfileModPin ToPin() => new(ModVersion.ModId, ModVersion.Id, Locked);
}
