using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// Every known version of every known mod, each mod's versions in one order.
/// </summary>
/// <remarks>
/// <para>
/// <b>The repo's own order is handed in as settled and never re-derived.</b> It was arbitrated once
/// and stored server-side, and clients on different adapter versions recomputing it would disagree
/// about what an update is. Only the unregistered versions are placed here, which is the one part
/// the repo has no answer for yet.
/// </para>
/// <para>
/// Built where a page or a planner needs it rather than stored on the catalog, because the set it is
/// over changes with every source toggle - see <see cref="ModVersionSet"/> for what one mod's entry
/// carries, and <c>ProfileModUpdates</c> for the one question that reads the abstentions.
/// </para>
/// </remarks>
public static class ModVersionIndex
{
    public static IReadOnlyDictionary<ModKey, ModVersionSet> Build(
        IEnumerable<CatalogModVersion> versions,
        IModVersionComparer comparer)
    {
        var result = new Dictionary<ModKey, ModVersionSet>();

        foreach (var group in versions.GroupBy(x => x.ModId))
        {
            // Deduplicated on the way in: the caller merges several sources and the draft's own
            // pins, and the same version can legitimately arrive from more than one of them.
            var byVersion = new Dictionary<ModVersionKey, CatalogModVersion>();

            foreach (var version in group)
            {
                byVersion.TryAdd(version.VersionId, version);
            }

            var settled = byVersion.Values
                .Where(x => x.SequenceNumber is not null)
                .OrderBy(x => x.SequenceNumber)
                .Select(x => x.VersionId)
                .ToList();

            var ordering = ModVersionPartialOrder.Derive([.. byVersion.Keys], comparer, settled);

            result[group.Key] = new ModVersionSet(
                [.. ordering.Order.Select(x => byVersion[x])],
                ordering.UnorderedPairs);
        }

        return result;
    }
}


/// <summary>
/// One mod's versions, oldest first, plus the pairs nothing settled.
/// </summary>
/// <remarks>
/// <b>The abstentions are kept rather than dropped</b>, which is the whole reason this is not a
/// list. A topological sort has to put every version somewhere, so two versions the comparer could
/// not place still come out one after the other - and treating that accidental adjacency as "newer"
/// is how a possible downgrade gets offered as an update. <see cref="IsAfter"/> is the only honest
/// reading of the order, and it asks both questions.
/// </remarks>
public sealed class ModVersionSet
{
    private readonly Dictionary<ModVersionKey, int> _positions;
    private readonly HashSet<ModVersionPair> _unordered;


    public ModVersionSet(IReadOnlyList<CatalogModVersion> order, IReadOnlyList<ModVersionPair> unordered)
    {
        Order = order;
        Unordered = unordered;

        _positions = order
            .Select((version, position) => (version, position))
            .ToDictionary(x => x.version.VersionId, x => x.position);

        _unordered = [.. unordered];

        NewestRegistered = order
            .Where(x => x.SequenceNumber is not null)
            .MaxBy(x => x.SequenceNumber);
    }


    /// <summary>Oldest first, so the last entry is the newest anything here can place.</summary>
    public IReadOnlyList<CatalogModVersion> Order { get; }

    /// <summary>
    /// The newest version the repo holds of this mod, or null where it holds none at all. Read from
    /// the stored sequence numbers rather than from the derived order, which is the same rule
    /// <c>RepoModsPageViewModel</c> applies: the repo arbitrated this once and a client re-deriving
    /// it would be a second opinion free to disagree.
    /// </summary>
    public CatalogModVersion? NewestRegistered { get; }

    /// <inheritdoc cref="ModVersionOrdering.UnorderedPairs"/>
    public IReadOnlyList<ModVersionPair> Unordered { get; }

    public CatalogModVersion? Find(ModVersionKey version)
        => _positions.TryGetValue(version, out var position) ? Order[position] : null;

    /// <summary>
    /// Whether the order places <paramref name="candidate"/> unambiguously after
    /// <paramref name="pin"/>. False for a pair the comparer abstained on, and false for either
    /// version being unknown here.
    /// </summary>
    public bool IsAfter(ModVersionKey candidate, ModVersionKey pin)
    {
        if (_positions.TryGetValue(candidate, out var right) is false
            || _positions.TryGetValue(pin, out var left) is false
            || right <= left)
        {
            return false;
        }

        // A pair is recorded with the earlier of the two first, so this is the only spelling of it.
        return _unordered.Contains(new ModVersionPair(pin, candidate)) is false;
    }

    /// <summary>
    /// Whether the order left <paramref name="candidate"/> genuinely uncompared against
    /// <see cref="NewestRegistered"/> - the third answer <see cref="IsAfter"/> collapses into "not
    /// after". A version this is true of may be exactly the one somebody came here to add, and the
    /// only reason it was not offered as an update is that the program could not tell; it is also
    /// exactly the version that will raise the arbitration dialog if it is imported, since that
    /// dialog exists for the same abstentions. False where the repo holds nothing of this mod at
    /// all - there is nothing to be uncompared against - and false for the newest registered version
    /// itself.
    /// </summary>
    public bool CouldNotCompareToNewest(ModVersionKey candidate)
    {
        if (NewestRegistered is not CatalogModVersion newest
            || candidate == newest.VersionId
            || _positions.ContainsKey(candidate) is false)
        {
            return false;
        }

        return _unordered.Contains(new ModVersionPair(newest.VersionId, candidate))
            || _unordered.Contains(new ModVersionPair(candidate, newest.VersionId));
    }
}
