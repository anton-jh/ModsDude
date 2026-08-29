namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// Orders a set of version strings by building the partial order the comparer describes and
/// topologically sorting it.
/// </summary>
/// <remarks>
/// Not <c>OrderBy</c>: a sort assumes a total order, and a comparer that abstains does not provide
/// one. Given <c>a</c> abstaining against <c>b</c>, a sort would place them by whatever the
/// partitioning happened to touch first and report nothing. All pairs are compared instead - a mod
/// has at most a few dozen versions, so the quadratic cost is not worth avoiding - and the pairs
/// left over are reported rather than invented.
/// </remarks>
public static class ModVersionPartialOrder
{
    /// <param name="settledOrder">
    /// Versions whose relative order is already established, in that order - the repo's stored
    /// ordering. They are taken as fact and never handed to the comparer, so an ordering the repo
    /// has already settled cannot come back as a question. Entries not in <paramref name="versions"/>
    /// are ignored.
    /// </param>
    public static ModVersionOrdering Derive(
        IReadOnlyList<string> versions,
        IModVersionComparer comparer,
        IReadOnlyList<string>? settledOrder = null)
    {
        var nodes = Distinct(versions);
        var settledPositions = SettledPositions(settledOrder, nodes);

        var precedes = new bool[nodes.Count, nodes.Count];
        var interchangeable = new bool[nodes.Count, nodes.Count];

        for (var left = 0; left < nodes.Count; left++)
        {
            for (var right = left + 1; right < nodes.Count; right++)
            {
                switch (CompareNodes(nodes, left, right, comparer, settledPositions))
                {
                    case ModVersionComparison.Earlier:
                        precedes[left, right] = true;
                        break;

                    case ModVersionComparison.Later:
                        precedes[right, left] = true;
                        break;

                    case ModVersionComparison.Equal:
                        interchangeable[left, right] = true;
                        interchangeable[right, left] = true;
                        break;
                }
            }
        }

        var reachable = TransitiveClosure(precedes);

        if (DropContradictions(precedes, reachable))
        {
            reachable = TransitiveClosure(precedes);
        }

        var order = TopologicalSort(nodes, precedes);

        return new ModVersionOrdering(order, UnorderedPairs(nodes, order, reachable, interchangeable));
    }


    private static ModVersionComparison CompareNodes(
        IReadOnlyList<string> nodes,
        int left,
        int right,
        IModVersionComparer comparer,
        IReadOnlyDictionary<string, int> settledPositions)
    {
        if (settledPositions.TryGetValue(nodes[left], out var settledLeft)
            && settledPositions.TryGetValue(nodes[right], out var settledRight))
        {
            return settledLeft < settledRight
                ? ModVersionComparison.Earlier
                : ModVersionComparison.Later;
        }

        return comparer.Compare(nodes[left], nodes[right]);
    }

    private static bool[,] TransitiveClosure(bool[,] precedes)
    {
        var count = precedes.GetLength(0);
        var reachable = (bool[,])precedes.Clone();

        for (var via = 0; via < count; via++)
        {
            for (var from = 0; from < count; from++)
            {
                if (!reachable[from, via])
                {
                    continue;
                }

                for (var to = 0; to < count; to++)
                {
                    if (reachable[via, to])
                    {
                        reachable[from, to] = true;
                    }
                }
            }
        }

        return reachable;
    }

    /// <summary>
    /// Drops the edges between versions that reach each other in both directions, which only a
    /// comparer contradicting itself can produce. Those versions come out mutually unordered and go
    /// to the user, rather than the sort throwing or picking a winner out of a cycle - an adapter
    /// may supply any comparer it likes, and one bad answer should cost a question, not the import.
    /// </summary>
    private static bool DropContradictions(bool[,] precedes, bool[,] reachable)
    {
        var count = precedes.GetLength(0);
        var dropped = false;

        for (var left = 0; left < count; left++)
        {
            for (var right = 0; right < count; right++)
            {
                if (left != right && reachable[left, right] && reachable[right, left] && precedes[left, right])
                {
                    precedes[left, right] = false;
                    dropped = true;
                }
            }
        }

        return dropped;
    }

    /// <summary>
    /// Kahn's algorithm, always taking the lowest-numbered version that is free to go next. Where
    /// the partial order leaves a choice, that choice falls to the caller's input order, so two
    /// runs over the same input cannot disagree.
    /// </summary>
    private static IReadOnlyList<string> TopologicalSort(IReadOnlyList<string> nodes, bool[,] precedes)
    {
        var placed = new bool[nodes.Count];
        var order = new List<string>(nodes.Count);

        for (var step = 0; step < nodes.Count; step++)
        {
            var next = -1;

            for (var candidate = 0; candidate < nodes.Count && next < 0; candidate++)
            {
                if (placed[candidate])
                {
                    continue;
                }

                var blocked = false;
                for (var other = 0; other < nodes.Count && !blocked; other++)
                {
                    blocked = !placed[other] && precedes[other, candidate];
                }

                if (!blocked)
                {
                    next = candidate;
                }
            }

            if (next < 0)
            {
                throw new InvalidOperationException("The version ordering still contains a cycle after contradictory comparisons were dropped.");
            }

            placed[next] = true;
            order.Add(nodes[next]);
        }

        return order;
    }

    private static IReadOnlyList<ModVersionPair> UnorderedPairs(
        IReadOnlyList<string> nodes,
        IReadOnlyList<string> order,
        bool[,] reachable,
        bool[,] interchangeable)
    {
        var positions = order
            .Select((version, position) => (version, position))
            .ToDictionary(x => x.version, x => x.position, StringComparer.Ordinal);

        var pairs = new List<ModVersionPair>();

        for (var left = 0; left < nodes.Count; left++)
        {
            for (var right = left + 1; right < nodes.Count; right++)
            {
                if (reachable[left, right] || reachable[right, left] || interchangeable[left, right])
                {
                    continue;
                }

                pairs.Add(positions[nodes[left]] < positions[nodes[right]]
                    ? new ModVersionPair(nodes[left], nodes[right])
                    : new ModVersionPair(nodes[right], nodes[left]));
            }
        }

        return [.. pairs.OrderBy(x => positions[x.First]).ThenBy(x => positions[x.Second])];
    }

    private static IReadOnlyList<string> Distinct(IReadOnlyList<string> versions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        return [.. versions.Where(seen.Add)];
    }

    private static IReadOnlyDictionary<string, int> SettledPositions(IReadOnlyList<string>? settledOrder, IReadOnlyList<string> nodes)
    {
        if (settledOrder is null)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var known = new HashSet<string>(nodes, StringComparer.Ordinal);

        return settledOrder
            .Where(known.Contains)
            .Select((version, position) => (version, position))
            .ToDictionary(x => x.version, x => x.position, StringComparer.Ordinal);
    }
}


/// <param name="Order">
/// Every version handed in, in the derived order. Where the order is under-determined but
/// consistent, the caller's input order breaks the tie.
/// </param>
/// <param name="UnorderedPairs">
/// The pairs nothing settled - neither directly nor through another version. These, and only these,
/// are questions for the user.
/// </param>
public sealed record ModVersionOrdering(
    IReadOnlyList<string> Order,
    IReadOnlyList<ModVersionPair> UnorderedPairs)
{
    public bool IsFullyOrdered => UnorderedPairs.Count == 0;
}


/// <summary>
/// Two versions the comparer left unordered. <paramref name="First"/> is whichever the derived
/// order happened to put first, so that a pair reads the same way twice.
/// </summary>
public readonly record struct ModVersionPair(string First, string Second);
