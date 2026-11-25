namespace ModsDude.Client.Core.Sync;

/// <summary>
/// A game whose mod folders are exactly what was applied, on a different revision of the profile
/// than the one it needs now.
/// </summary>
/// <param name="From">The applied revision. Across several folders, the oldest of them.</param>
/// <param name="To">The revision the game needs now.</param>
public sealed record RevisionGap(int From, int To)
{
    /// <summary>What the button that puts the game on <see cref="To"/> says.</summary>
    public string ActionLabel => To > From ? $"Update to revision {To}" : $"Re-apply revision {To}";

    /// <summary>The gap of one folder, or null where it has drifted any other way.</summary>
    public static RevisionGap? Of(DriftReport report)
        => report.IsProfileMoveOnly
            && report.AppliedRevision is int applied
            && report.CurrentRevision is int current
                ? new RevisionGap(applied, current)
                : null;

    /// <summary>
    /// The gap of a game's drifted folders, or null where any of them has drifted any other way or
    /// holds a drifted savegame.
    /// </summary>
    public static RevisionGap? Of(IReadOnlyCollection<DriftReport> reports)
    {
        if (reports.Count == 0 || reports.Any(x => x.HasSavegameDrift))
        {
            return null;
        }

        var gaps = reports.Select(Of).OfType<RevisionGap>().ToList();

        return gaps.Count == reports.Count
            ? new RevisionGap(gaps.Min(x => x.From), gaps.Max(x => x.To))
            : null;
    }
}
