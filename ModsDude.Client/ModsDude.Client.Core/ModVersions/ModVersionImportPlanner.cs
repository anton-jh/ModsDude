using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// Orders a whole import up front, splitting it into the mods that can be registered straight away
/// and the ones whose ordering needs a person.
/// </summary>
/// <remarks>
/// The split is what keeps the question affordable: everything the comparer settled proceeds
/// immediately and never waits on the dialog, which is asked once for the whole import rather than
/// once per mod - and if it is cancelled, only the mods in it are skipped. One unorderable mod is
/// one mod's problem, not a reason to lose a two-thousand-mod batch.
/// </remarks>
public static class ModVersionImportPlanner
{
    public static ModVersionImportPlan Plan(IEnumerable<ModVersionImportCandidate> candidates, IModVersionComparer comparer)
    {
        var ready = new List<ModVersionPlacementPlan>();
        var arbitration = new List<ModVersionArbitrationItem>();

        foreach (var candidate in candidates)
        {
            var plan = ModVersionPlacementPlanner.Plan(candidate.ModId, candidate.RegisteredInOrder, candidate.Incoming, comparer);

            if (plan.NeedsArbitration)
            {
                arbitration.Add(ModVersionArbitrationItem.For(plan, candidate));
            }
            else
            {
                ready.Add(plan);
            }
        }

        return new ModVersionImportPlan(ready, arbitration);
    }
}


public sealed record ModVersionImportCandidate(
    ModKey ModId,
    IReadOnlyList<ModVersionKey> RegisteredInOrder,
    IReadOnlyList<ModVersionKey> Incoming);


/// <param name="Ready">Mods whose ordering the comparer settled. These register without waiting.</param>
/// <param name="Arbitration">
/// Everything the arbitration dialog needs, one entry per mod. Empty when nothing is ambiguous.
/// </param>
public sealed record ModVersionImportPlan(
    IReadOnlyList<ModVersionPlacementPlan> Ready,
    IReadOnlyList<ModVersionArbitrationItem> Arbitration)
{
    public bool NeedsArbitration => Arbitration.Count > 0;

    /// <summary>
    /// What cancelling the dialog costs: these mods stay unregistered and can be imported again
    /// later, and the rest of the import is unaffected.
    /// </summary>
    public IReadOnlyList<ModKey> ModIdsSkippedByCancelling => [.. Arbitration.Select(x => x.ModId)];
}


/// <param name="Versions">
/// The mod's versions in the order that <i>was</i> derived, with the ones nothing could place
/// marked - so the dialog shows a list that is already mostly right and asks only about the rest.
/// </param>
/// <param name="UnorderedPairs">The pairs that made this mod a question.</param>
public sealed record ModVersionArbitrationItem(
    ModKey ModId,
    IReadOnlyList<ModVersionArbitrationEntry> Versions,
    IReadOnlyList<ModVersionPair> UnorderedPairs)
{
    public IReadOnlyList<ModVersionKey> RegisteredInOrder => [.. Versions.Where(x => !x.IsIncoming).Select(x => x.VersionId)];

    public IReadOnlyList<ModVersionKey> Incoming => [.. Versions.Where(x => x.IsIncoming).Select(x => x.VersionId)];

    internal static ModVersionArbitrationItem For(ModVersionPlacementPlan plan, ModVersionImportCandidate candidate)
    {
        var registered = new HashSet<ModVersionKey>(candidate.RegisteredInOrder);

        var unplaceable = new HashSet<ModVersionKey>(
            plan.UnorderedPairs.SelectMany(x => new[] { x.First, x.Second }));

        return new ModVersionArbitrationItem(
            plan.ModId,
            [.. plan.Order.Select(x => Entry(x, registered, unplaceable))],
            plan.UnorderedPairs);
    }

    private static ModVersionArbitrationEntry Entry(ModVersionKey version, IReadOnlySet<ModVersionKey> registered, IReadOnlySet<ModVersionKey> unplaceable)
    {
        var isIncoming = !registered.Contains(version);

        // A registered version can appear in an unordered pair, but it is not the one that needs
        // placing: it is already where the repo put it, and a placement can only insert around it.
        return new ModVersionArbitrationEntry(version, isIncoming, isIncoming && unplaceable.Contains(version));
    }
}


/// <param name="IsIncoming">False for a version the repo already holds, which the user may not move.</param>
/// <param name="IsUnplaceable">Whether the ordering failed to place this incoming version.</param>
public sealed record ModVersionArbitrationEntry(ModVersionKey VersionId, bool IsIncoming, bool IsUnplaceable);
