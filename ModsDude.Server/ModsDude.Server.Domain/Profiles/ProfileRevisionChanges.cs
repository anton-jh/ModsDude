namespace ModsDude.Server.Domain.Profiles;

/// <summary>
/// What one revision did to the one before it, as three counts.
/// </summary>
/// <remarks>
/// Counts rather than the changed mods themselves: the history list renders one line per revision,
/// and what somebody scanning it wants is the shape of the change.
/// <para>
/// A record rather than a struct because it is mapped as an EF complex property, the way
/// <see cref="Repos.AdapterData"/> is - three columns on the revision's own row.
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
