namespace ModsDude.Server.Domain.Mods;

/// <summary>
/// Keeps <see cref="ModVersion.SequenceNumber"/> contiguous across the versions sharing one
/// <c>(RepoId, ModId)</c>. Every method takes that sibling set, because flattening
/// <c>Mod</c> away left the ordering with no parent entity to hang on.
/// </summary>
public static class ModVersionSequencer
{
    /// <summary>
    /// Whether a placement is still valid against the ordering as it stands. A placement names the
    /// two versions the new one goes between, and both are asserted: relative placement against a
    /// single neighbour stops collisions but still permits a silently wrong order when two members
    /// insert against a state neither has seen the other change, which offers a downgrade as an
    /// upgrade. A violation is therefore a rejection rather than something to repair — the client
    /// refetches, recomputes the placement and retries.
    /// </summary>
    public static bool CheckPlacementIsValid(IReadOnlyCollection<ModVersion> siblings, ModVersionId? after, ModVersionId? before)
    {
        var afterVersion = FindPlacementNeighbour(siblings, after);
        var beforeVersion = FindPlacementNeighbour(siblings, before);

        if ((after is not null && afterVersion is null) || (before is not null && beforeVersion is null))
        {
            return false;
        }

        if (afterVersion is null && beforeVersion is null)
        {
            return siblings.Count == 0;
        }

        if (afterVersion is null)
        {
            return beforeVersion!.SequenceNumber == siblings.Min(x => x.SequenceNumber);
        }

        if (beforeVersion is null)
        {
            return afterVersion.SequenceNumber == siblings.Max(x => x.SequenceNumber);
        }

        return beforeVersion.SequenceNumber == afterVersion.SequenceNumber + 1;
    }

    /// <summary>
    /// Shifts the siblings that follow the placement up by one and returns the sequence number the
    /// new version takes. Call <see cref="CheckPlacementIsValid"/> first.
    /// </summary>
    public static int MakeRoomAt(IReadOnlyCollection<ModVersion> siblings, ModVersionId? after, ModVersionId? before, DateTimeOffset timestamp)
    {
        if (!CheckPlacementIsValid(siblings, after, before))
        {
            throw new InvalidOperationException($"Cannot place a version after '{after}' and before '{before}'. The placement does not match the current version order");
        }

        if (before is null)
        {
            var lastSequenceNumber = siblings.MaxBy(x => x.SequenceNumber)?.SequenceNumber ?? -1;

            return lastSequenceNumber + 1;
        }

        // Captured before the shift below, which moves the version it was read from out from under it.
        var insertAt = siblings.First(x => x.Id == before.Value).SequenceNumber;

        // Materialized: the predicate reads a sequence number that the loop body mutates.
        var allFollowing = siblings
            .Where(x => x.SequenceNumber >= insertAt)
            .ToList();

        foreach (var version in allFollowing)
        {
            version.SequenceNumber++;
            version.Updated = timestamp;
        }

        return insertAt;
    }

    /// <summary>
    /// Closes the gap left by a removed version. <paramref name="siblings"/> must no longer contain
    /// <paramref name="removed"/>.
    /// </summary>
    public static void CloseGap(IReadOnlyCollection<ModVersion> siblings, ModVersion removed, DateTimeOffset timestamp)
    {
        // Materialized for the same reason as in MakeRoomAt: the loop body mutates the sequence
        // number the predicate reads.
        var newerVersions = siblings
            .Where(x => x.SequenceNumber > removed.SequenceNumber)
            .ToList();

        foreach (var newerVersion in newerVersions)
        {
            newerVersion.SequenceNumber--;
            newerVersion.Updated = timestamp;
        }
    }


    private static ModVersion? FindPlacementNeighbour(IReadOnlyCollection<ModVersion> siblings, ModVersionId? id)
        => id is null ? null : siblings.FirstOrDefault(x => x.Id == id.Value);
}
