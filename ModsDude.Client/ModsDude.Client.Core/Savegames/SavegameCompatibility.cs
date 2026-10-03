using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Savegames;

/// <summary>
/// How far the latest mod list has moved from the one a savegame was last played on.
/// </summary>
/// <param name="Comparison">The two revisions compared directly, ignoring every revision between them.</param>
/// <param name="Score">The weighted sum of the changes, per the adapter's policy.</param>
/// <param name="LockedChanges">
/// Mods locked on the revision the save was played on whose version changed, or that were removed.
/// </param>
/// <param name="ShouldPrompt">Whether checking out on the latest revision asks first.</param>
public sealed record SavegameCompatibilityVerdict(
    ProfileRevisionComparison Comparison,
    int Score,
    IReadOnlyList<ProfileModChange> LockedChanges,
    bool ShouldPrompt)
{
    public int AddedCount => Comparison.AddedCount;
    public int RemovedCount => Comparison.RemovedCount;

    /// <summary>Mods at another version. A lock toggled on its own changes no file and is not counted.</summary>
    public int VersionChangedCount => Comparison.Changes.Count(x => x.VersionMoved);
}


/// <summary>Scores a mod list change against a savegame. Pure; the comparison is read around it.</summary>
public static class SavegameCompatibility
{
    public static SavegameCompatibilityVerdict Assess(ProfileRevisionComparison comparison, SavegameCompatibilityPolicy policy)
    {
        var score = 0;
        var locked = new List<ProfileModChange>();

        foreach (var change in comparison.Changes)
        {
            score += change.Kind switch
            {
                ProfileModChangeKind.Added => policy.AddedWeight,
                ProfileModChangeKind.Removed => policy.RemovedWeight,
                ProfileModChangeKind.Changed when change.VersionMoved => policy.ChangedWeight,
                _ => 0
            };

            // The older side's lock only: a mod that becomes locked in the newer revision was free to
            // move while this save was played on it.
            if (change.FromLock.IsLocked && (change.Kind is ProfileModChangeKind.Removed || change.VersionMoved))
            {
                locked.Add(change);
            }
        }

        return new SavegameCompatibilityVerdict(
            comparison,
            score,
            locked,
            locked.Count > 0 || score >= policy.PromptThreshold);
    }
}
