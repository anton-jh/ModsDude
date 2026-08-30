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
        return CheckPlacementIsValidAmong(Ordered(siblings), after, before);
    }

    /// <summary>
    /// Whether a placement is still valid for moving <paramref name="moved"/>, which is asserted the
    /// same way and for the same reason as a placement for a new version — a move against one
    /// neighbour still permits a silently wrong order when two members act on a state neither has
    /// seen the other change. The neighbours are named among the siblings <em>with the moved version
    /// taken out</em>, because that is the ordering it is being placed into.
    /// </summary>
    public static bool CheckMoveIsValid(IReadOnlyCollection<ModVersion> siblings, ModVersion moved, ModVersionId? after, ModVersionId? before)
    {
        return CheckPlacementIsValidAmong(OrderedWithout(siblings, moved), after, before);
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
    /// Whether the move would actually change the ordering. A move that lands a version where it
    /// already sits is accepted rather than refused — a client rewriting an order should not have to
    /// special-case the entry that turned out not to move — but it is worth not writing, since
    /// writing it costs two statements and a transaction to arrive back where it started.
    /// </summary>
    public static bool CheckMoveChangesTheOrder(IReadOnlyCollection<ModVersion> siblings, ModVersion moved, ModVersionId? after, ModVersionId? before)
    {
        var remaining = OrderedWithout(siblings, moved);

        var landsAt = FindPlacementNeighbour(remaining, before) ?? remaining.Count;
        var sitsAt = siblings.Count(x => x.SequenceNumber < moved.SequenceNumber);

        return landsAt != sitsAt;
    }

    /// <summary>
    /// Frees the slot <paramref name="moved"/> sits in by parking it past the end of the ordering.
    /// <b>The first half of a move, and it has to reach the database before the second half runs.</b>
    /// </summary>
    /// <remarks>
    /// A move is a rotation: every row in the range takes the slot of the next, and the one being
    /// moved takes the slot at the far end. There is no order in which a rotation's rows can be
    /// written one at a time without two of them briefly holding the same sequence number, which the
    /// unique index on <c>(RepoId, ModId, SequenceNumber)</c> forbids — an insert or a removal is a
    /// chain and orders fine, a rotation is a cycle and does not. Parking is the temporary slot that
    /// breaks the cycle into a chain. It leaves the ordering non-contiguous, so the two halves belong
    /// in one transaction.
    /// </remarks>
    public static void VacateForMove(IReadOnlyCollection<ModVersion> siblings, ModVersion moved, DateTimeOffset timestamp)
    {
        if (!siblings.Contains(moved))
        {
            throw new InvalidOperationException($"Cannot move version '{moved.Id.Value}' of mod '{moved.ModId.Value}'. It is not among the siblings supplied");
        }

        moved.SequenceNumber = siblings.Max(x => x.SequenceNumber) + 1;
        moved.Updated = timestamp;
    }

    /// <summary>
    /// Moves an already-registered version to a new placement, closing the gap it leaves and making
    /// room where it lands so that the sequence stays contiguous. <paramref name="siblings"/>
    /// includes <paramref name="moved"/>; the placement names the two versions it goes between in
    /// the ordering without it. Call <see cref="CheckMoveIsValid"/> first, and against a database
    /// apply <see cref="VacateForMove"/> first as well. A move that lands where the version already
    /// sits is a no-op and stamps nothing.
    /// </summary>
    /// <remarks>
    /// A move shifts a <em>range</em> — everything between where the version left and where it
    /// landed — rather than everything past a point, which is the one way it differs from an insert
    /// or a removal. Rows outside that range keep the numbers they had, so nothing writes them.
    /// </remarks>
    public static void MoveTo(IReadOnlyCollection<ModVersion> siblings, ModVersion moved, ModVersionId? after, ModVersionId? before, DateTimeOffset timestamp)
    {
        if (!siblings.Contains(moved))
        {
            throw new InvalidOperationException($"Cannot move version '{moved.Id.Value}' of mod '{moved.ModId.Value}'. It is not among the siblings supplied");
        }

        // Materialized before anything is mutated, for the same reason as in MakeRoomAt: every
        // position below is read from sequence numbers that the renumbering changes.
        var remaining = OrderedWithout(siblings, moved);

        if (!CheckPlacementIsValidAmong(remaining, after, before))
        {
            throw new InvalidOperationException($"Cannot move version '{moved.Id.Value}' of mod '{moved.ModId.Value}' to sit after '{after}' and before '{before}'. The placement does not match the current version order");
        }

        // Captured against the ordering as it stands, before the renumbering moves the version it
        // was read from out from under it.
        var landsAt = FindPlacementNeighbour(remaining, before) ?? remaining.Count;

        var reordered = new List<ModVersion>(remaining);
        reordered.Insert(landsAt, moved);

        for (var index = 0; index < reordered.Count; index++)
        {
            var version = reordered[index];

            if (version.SequenceNumber == index)
            {
                continue;
            }

            version.SequenceNumber = index;
            version.Updated = timestamp;
        }
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


    /// <summary>
    /// Adjacency in the ordering rather than arithmetic on sequence numbers. The two agree for a
    /// contiguous set, but a move validates against the siblings with the moved version taken out,
    /// which leaves a gap where it sat — and a position is what a placement names anyway; the
    /// numbers are only how the order is stored.
    /// </summary>
    private static bool CheckPlacementIsValidAmong(IReadOnlyList<ModVersion> ordered, ModVersionId? after, ModVersionId? before)
    {
        var afterIndex = FindPlacementNeighbour(ordered, after);
        var beforeIndex = FindPlacementNeighbour(ordered, before);

        if ((after is not null && afterIndex is null) || (before is not null && beforeIndex is null))
        {
            return false;
        }

        if (afterIndex is null && beforeIndex is null)
        {
            return ordered.Count == 0;
        }

        if (afterIndex is null)
        {
            return beforeIndex == 0;
        }

        if (beforeIndex is null)
        {
            return afterIndex == ordered.Count - 1;
        }

        return beforeIndex == afterIndex + 1;
    }

    private static int? FindPlacementNeighbour(IReadOnlyList<ModVersion> ordered, ModVersionId? id)
    {
        if (id is null)
        {
            return null;
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Id == id.Value)
            {
                return index;
            }
        }

        return null;
    }

    private static IReadOnlyList<ModVersion> Ordered(IEnumerable<ModVersion> siblings)
        => [.. siblings.OrderBy(x => x.SequenceNumber)];

    private static IReadOnlyList<ModVersion> OrderedWithout(IEnumerable<ModVersion> siblings, ModVersion excluded)
        => Ordered(siblings.Where(x => x != excluded));
}
