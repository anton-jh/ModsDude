using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.ModVersions;

/// <summary>
/// Works out where each incoming version of one mod goes, as placements the register endpoint
/// accepts.
/// </summary>
/// <remarks>
/// <para>
/// Positions are computed against the <b>final</b> intended order and then emitted in ascending
/// order, each step inserting before the next version the server already knows about. Every step is
/// therefore valid on its own and no batch-placement API is needed. Registering at a provisional
/// position and repairing it afterwards is not an option: in the interim the newest version would
/// be wrong, and anything appended past the real newest advertises itself as an update and offers
/// everybody a downgrade.
/// </para>
/// <para>
/// Inserting ahead of a registered version moves that version's sequence number. That is expected -
/// the server shifts the rows that follow - and it is why the ordering has to be settled before the
/// first registration rather than between them.
/// </para>
/// </remarks>
public static class ModVersionPlacementPlanner
{
    /// <param name="registeredInOrder">The versions the repo already holds, in their stored order.</param>
    /// <param name="incoming">
    /// The versions about to be registered. Any that are already registered are ignored - the
    /// import treats a version that is already there as a success, not as something to place.
    /// </param>
    public static ModVersionPlacementPlan Plan(
        ModKey modId,
        IReadOnlyList<ModVersionKey> registeredInOrder,
        IReadOnlyList<ModVersionKey> incoming,
        IModVersionComparer comparer)
    {
        var registered = new HashSet<ModVersionKey>(registeredInOrder);
        var toPlace = Distinct(incoming.Where(x => !registered.Contains(x)));

        var ordering = ModVersionPartialOrder.Derive([.. registeredInOrder, .. toPlace], comparer, registeredInOrder);

        if (!ordering.IsFullyOrdered)
        {
            return new ModVersionPlacementPlan(modId, ordering.Order, [], ordering.UnorderedPairs);
        }

        return new ModVersionPlacementPlan(modId, ordering.Order, Registrations(registered, toPlace, ordering.Order), []);
    }

    /// <summary>
    /// The same placements from an order the user arbitrated rather than one the comparer derived.
    /// </summary>
    /// <param name="resolvedOrder">
    /// Every registered and incoming version, in the intended final order. The registered versions
    /// must keep their stored relative order: placements only insert, so nothing here can move one
    /// registered version past another.
    /// </param>
    public static ModVersionPlacementPlan PlanFor(
        ModKey modId,
        IReadOnlyList<ModVersionKey> registeredInOrder,
        IReadOnlyList<ModVersionKey> incoming,
        IReadOnlyList<ModVersionKey> resolvedOrder)
    {
        var registered = new HashSet<ModVersionKey>(registeredInOrder);
        var toPlace = Distinct(incoming.Where(x => !registered.Contains(x)));

        var expected = new HashSet<ModVersionKey>(registeredInOrder.Concat(toPlace));
        if (!expected.SetEquals(resolvedOrder) || resolvedOrder.Count != expected.Count)
        {
            throw new ArgumentException("The resolved order must contain every registered and incoming version exactly once.", nameof(resolvedOrder));
        }

        if (!resolvedOrder.Where(registered.Contains).SequenceEqual(registeredInOrder))
        {
            throw new ArgumentException("The resolved order may not reorder versions that are already registered.", nameof(resolvedOrder));
        }

        return new ModVersionPlacementPlan(modId, resolvedOrder, Registrations(registered, toPlace, resolvedOrder), []);
    }


    private static IReadOnlyList<ModVersionRegistration> Registrations(
        IReadOnlySet<ModVersionKey> registered,
        IReadOnlyList<ModVersionKey> toPlace,
        IReadOnlyList<ModVersionKey> finalOrder)
    {
        var placing = new HashSet<ModVersionKey>(toPlace);
        var known = new HashSet<ModVersionKey>(registered);
        var registrations = new List<ModVersionRegistration>(toPlace.Count);

        for (var position = 0; position < finalOrder.Count; position++)
        {
            if (!placing.Contains(finalOrder[position]))
            {
                continue;
            }

            // Ascending, so everything ahead of this version is already registered - the one
            // immediately before it in the final order is the version it will really follow.
            var after = position > 0 ? finalOrder[position - 1] : (ModVersionKey?)null;

            var before = finalOrder
                .Skip(position + 1)
                .Cast<ModVersionKey?>()
                .FirstOrDefault(x => known.Contains(x!.Value));

            registrations.Add(new ModVersionRegistration(finalOrder[position], new ModVersionPlacement(after, before)));
            known.Add(finalOrder[position]);
        }

        return registrations;
    }

    private static IReadOnlyList<ModVersionKey> Distinct(IEnumerable<ModVersionKey> versions)
    {
        var seen = new HashSet<ModVersionKey>();

        return [.. versions.Where(seen.Add)];
    }
}


/// <param name="Order">The intended final order of every registered and incoming version.</param>
/// <param name="Registrations">
/// What to send, in the order to send it. Empty when <see cref="NeedsArbitration"/> - a version is
/// never registered at a position the ordering did not settle.
/// </param>
public sealed record ModVersionPlacementPlan(
    ModKey ModId,
    IReadOnlyList<ModVersionKey> Order,
    IReadOnlyList<ModVersionRegistration> Registrations,
    IReadOnlyList<ModVersionPair> UnorderedPairs)
{
    public bool NeedsArbitration => UnorderedPairs.Count > 0;
}


public sealed record ModVersionRegistration(ModVersionKey VersionId, ModVersionPlacement Placement);


/// <summary>
/// The two versions the new one goes between. Both are asserted server-side: naming only one stops
/// collisions but still allows a silently wrong order when two members insert against a state
/// neither has seen the other change.
/// </summary>
/// <param name="After">Null when the version goes first, which asserts that <paramref name="Before"/> is currently the oldest.</param>
/// <param name="Before">Null when the version is appended, which asserts that <paramref name="After"/> is currently the newest.</param>
public sealed record ModVersionPlacement(ModVersionKey? After, ModVersionKey? Before);
