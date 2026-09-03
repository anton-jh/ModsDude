using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// One mod, pinned at one version, with the profile's own lock - a <see cref="ModDependency"/> with
/// the version's whole record left behind.
/// </summary>
/// <remarks>
/// This is the form a comparison works in. Diffing two revisions needs three fields per mod, and
/// materializing two snapshots' worth of <see cref="ModVersion"/> entities - each dragging its owned
/// attribute and image collections - to read them is exactly the cost a summary exists to avoid.
/// </remarks>
public readonly record struct ProfileModPin(ModId ModId, ModVersionId VersionId, bool Locked);


/// <summary>
/// What one revision did to the one before it, as three counts.
/// </summary>
/// <remarks>
/// Counts rather than the changed mods themselves. The history list renders one line per revision,
/// and a profile holds one to two thousand mods; what somebody scanning it wants is the shape of the
/// change, and the two snapshots are still there for anybody who wants the rest.
/// <para>
/// A record rather than a struct because it is mapped as an EF complex property, the way
/// <see cref="Repos.AdapterData"/> is - three columns on the revision's own row rather than a table
/// of its own.
/// </para>
/// </remarks>
public record ProfileRevisionChanges(int Added, int Changed, int Removed)
{
    public static ProfileRevisionChanges None { get; } = new(0, 0, 0);


    public bool IsEmpty => Added is 0 && Changed is 0 && Removed is 0;


    /// <summary>
    /// Keyed by mod, because a profile pins each mod exactly once - so a mod that moved version or
    /// had its lock toggled is a change rather than a removal and an addition.
    /// </summary>
    public static ProfileRevisionChanges Between(IEnumerable<ProfileModPin> before, IEnumerable<ProfileModPin> after)
    {
        var previous = before.ToDictionary(x => x.ModId);
        var desired = after.ToDictionary(x => x.ModId);

        var added = 0;
        var changed = 0;

        foreach (var (modId, pin) in desired)
        {
            if (previous.TryGetValue(modId, out var existing) is false)
            {
                added++;
            }
            else if (existing.VersionId != pin.VersionId || existing.Locked != pin.Locked)
            {
                changed++;
            }
        }

        var removed = previous.Keys.Count(x => desired.ContainsKey(x) is false);

        return new ProfileRevisionChanges(added, changed, removed);
    }
}
