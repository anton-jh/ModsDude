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
    /// A mod is held in a profile's mod list when either the profile pins it or the adapter marked
    /// the mod itself version-sensitive. Keeping the disjunction here puts the rule in one place.
    /// </summary>
    public bool IsEffectivelyLocked => Locked || ModVersion.Locked;


    public ProfileModPin ToPin() => new(ModVersion.ModId, ModVersion.Id, Locked);
}
