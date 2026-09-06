using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// What changed between two revisions of one profile, mod by mod.
/// </summary>
/// <remarks>
/// <para>
/// The revision's own <c>Changes</c> summary says how many; this says which. They are two different
/// costs and belong in two different places: the counts are three integers recorded when the
/// revision was written, so a history list can render fifty of them, and this is a comparison of two
/// whole snapshots, so it is computed when somebody asks for one.
/// </para>
/// <para>
/// <b>Computed on the client, out of two reads.</b> There is no diff endpoint, and adding one would
/// have bought little: naming the mods needs their registered records, which the client resolves
/// from a catalog walk that dwarfs the dependency rows either way. Keeping it here also keeps the
/// property that there is exactly one route into a profile's mod list, and that it reads.
/// </para>
/// </remarks>
public sealed record ProfileRevisionComparison(
    int From,
    int To,
    IReadOnlyList<ProfileModChange> Changes)
{
    public static ProfileRevisionComparison Empty { get; } = new(0, 0, []);


    public int AddedCount => Changes.Count(x => x.Kind is ProfileModChangeKind.Added);
    public int RemovedCount => Changes.Count(x => x.Kind is ProfileModChangeKind.Removed);
    public int ChangedCount => Changes.Count(x => x.Kind is ProfileModChangeKind.Changed);

    public bool IsEmpty => Changes.Count == 0;


    /// <summary>
    /// Keyed by mod, because a profile pins each mod exactly once - so a mod that moved version or
    /// had its lock toggled is one row saying what it moved from, rather than a removal and an
    /// addition the reader has to pair up themselves.
    /// </summary>
    /// <remarks>
    /// A changed row carries the version from the <paramref name="after"/> side and a removed row
    /// the version from <paramref name="before"/>: the record is there to render the mod, and the
    /// one that describes it is the one on the side it still exists on.
    /// </remarks>
    public static ProfileRevisionComparison Between(
        int from,
        int to,
        IReadOnlyList<PinnedMod> before,
        IReadOnlyList<PinnedMod> after)
    {
        var previous = before.ToDictionary(x => x.ModId);
        var desired = after.ToDictionary(x => x.ModId);

        var changes = new List<ProfileModChange>();

        foreach (var pin in after)
        {
            if (previous.TryGetValue(pin.ModId, out var existing) is false)
            {
                changes.Add(new ProfileModChange(
                    pin.Version,
                    ProfileModChangeKind.Added,
                    null,
                    pin.VersionId,
                    false,
                    pin.Lock.ByProfile));

                continue;
            }

            if (existing.VersionId == pin.VersionId && existing.Lock.ByProfile == pin.Lock.ByProfile)
            {
                continue;
            }

            changes.Add(new ProfileModChange(
                pin.Version,
                ProfileModChangeKind.Changed,
                existing.VersionId,
                pin.VersionId,
                existing.Lock.ByProfile,
                pin.Lock.ByProfile));
        }

        foreach (var pin in before)
        {
            if (desired.ContainsKey(pin.ModId) is false)
            {
                changes.Add(new ProfileModChange(
                    pin.Version,
                    ProfileModChangeKind.Removed,
                    pin.VersionId,
                    null,
                    pin.Lock.ByProfile,
                    false));
            }
        }

        return new ProfileRevisionComparison(
            from,
            to,
            [.. changes.OrderBy(x => x.Kind).ThenBy(x => x.DisplayName, NaturalOrder.Comparer)]);
    }
}


/// <param name="Version">
/// The mod as the side it still exists on describes it - the newer one, except for a removal.
/// </param>
/// <param name="FromVersionId">What the older revision pinned, or <c>null</c> where it pinned nothing.</param>
/// <param name="ToVersionId">What the newer one pins, or <c>null</c> where it pins nothing.</param>
public sealed record ProfileModChange(
    CatalogModVersion Version,
    ProfileModChangeKind Kind,
    ModVersionKey? FromVersionId,
    ModVersionKey? ToVersionId,
    bool FromLocked,
    bool ToLocked)
{
    public ModKey ModId => Version.ModId;
    public string DisplayName => Version.Name;

    public bool VersionMoved => Kind is ProfileModChangeKind.Changed && FromVersionId != ToVersionId;

    /// <summary>
    /// Only the profile's own lock. The adapter's belongs to the mod version rather than to the
    /// revision, so a mod re-registered as version-sensitive would otherwise read as a change
    /// somebody made to the profile.
    /// </summary>
    public bool LockChanged => Kind is ProfileModChangeKind.Changed && FromLocked != ToLocked;
}

/// <remarks>
/// The order is the order a reader wants them in: what is new, then what moved, then what is gone.
/// </remarks>
public enum ProfileModChangeKind
{
    Added,
    Changed,
    Removed
}
