using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// What has to be written to turn the profile's saved mod list into the one on screen.
/// </summary>
/// <remarks>
/// The editor is a draft until Save, so this is computed once at the end rather than one call per
/// click. That is also what makes Cancel mean something: a local-only mod moved into the list is
/// pending, and discarding the draft discards it without ever having uploaded anything.
/// See docs/09-mod-catalog.md#import-on-save.
/// </remarks>
public static class ProfileModListDiff
{
    public static ProfileModListChanges Compute(
        IEnumerable<ProfileModPin> original,
        IEnumerable<ProfileModPin> desired)
    {
        var before = original.ToDictionary(x => x.ModId);
        var after = desired.ToDictionary(x => x.ModId);

        var added = new List<ProfileModPin>();
        var changed = new List<ProfileModPin>();

        foreach (var (modId, pin) in after)
        {
            if (before.TryGetValue(modId, out var existing) is false)
            {
                added.Add(pin);
            }
            else if (Differs(existing, pin))
            {
                changed.Add(pin);
            }
        }

        var removed = before.Keys.Where(x => after.ContainsKey(x) is false).ToList();

        return new ProfileModListChanges(added, changed, removed);
    }

    /// <summary>
    /// The adapter's flag is deliberately not compared. It belongs to the mod version rather than to
    /// the dependency, and there is no request field that could carry it - a client that thought it
    /// had changed would write a dependency update that says nothing.
    /// </summary>
    private static bool Differs(ProfileModPin existing, ProfileModPin desired)
    {
        return existing.VersionId != desired.VersionId
            || existing.Lock.ByProfile != desired.Lock.ByProfile;
    }
}

public sealed record ProfileModListChanges(
    IReadOnlyList<ProfileModPin> Added,
    IReadOnlyList<ProfileModPin> Changed,
    IReadOnlyList<ModKey> Removed)
{
    public static ProfileModListChanges None { get; } = new([], [], []);

    public int Count => Added.Count + Changed.Count + Removed.Count;

    public bool IsEmpty => Count == 0;
}
