using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Profiles;

/// <summary>
/// Why a row of the editor's left list is set apart from the rest, if it is.
/// </summary>
public enum IgnoreState
{
    /// <summary>An ordinary row.</summary>
    None,

    /// <summary>Somebody ignored this mod, and it is not pinned.</summary>
    Ignored,

    /// <summary>
    /// A version of a mod the profile pins and holds in place. The pin is not going to move, so the
    /// row is noise for as long as the lock stands - and it is not something anybody decided, which
    /// is why it cannot be un-ignored from the row.
    /// </summary>
    OtherVersionOfLocked,

    /// <summary>
    /// The profile pins the mod at some other version, unlocked. Not ignorable, and not ignored: the
    /// row is an update to something the profile holds, which is exactly what it should show.
    /// </summary>
    Pinned
}

public static class ProfileIgnoring
{
    /// <summary>
    /// Which of the four this row is, given what the profile ignores and what this draft pins.
    /// </summary>
    /// <param name="modId">The mod the row stands for.</param>
    /// <param name="rowVersion">The version the row is showing.</param>
    /// <param name="ignored">The mods the profile ignores, as the server holds them.</param>
    /// <param name="pinned">What the draft pins of each mod, and whether it holds that in place.</param>
    /// <remarks>
    /// <b>The pin answers first.</b> A pinned mod cannot also be ignored, so a mod somebody ignored and
    /// this draft has since pinned is not ignored in this draft - which is what lets pinning an ignored
    /// mod be undone by discarding, with the server's list untouched until a save says otherwise.
    /// </remarks>
    public static IgnoreState Classify(
        ModKey modId,
        ModVersionKey rowVersion,
        IReadOnlySet<ModKey> ignored,
        IReadOnlyDictionary<ModKey, (ModVersionKey Version, bool Locked)> pinned)
    {
        if (pinned.TryGetValue(modId, out var held))
        {
            return held.Locked && held.Version != rowVersion
                ? IgnoreState.OtherVersionOfLocked
                : IgnoreState.Pinned;
        }

        return ignored.Contains(modId) ? IgnoreState.Ignored : IgnoreState.None;
    }

    /// <summary>
    /// The ignore list as it is written: what is ignored, minus what the draft pins.
    /// </summary>
    /// <remarks>
    /// The server refuses a list that overlaps what the profile pins, so this is what keeps a save from
    /// being refused - and it is computed rather than applied to the draft, so discarding a pin puts the
    /// ignore back.
    /// </remarks>
    public static HashSet<ModKey> WithoutPinned(IEnumerable<ModKey> ignored, IReadOnlySet<ModKey> pinned)
        => [.. ignored.Where(x => pinned.Contains(x) is false)];
}
