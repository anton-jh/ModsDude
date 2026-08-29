using ModsDude.Client.Core.ModVersions;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.ModVersions;

public class ModVersionImportPlannerTests
{
    private static readonly DefaultModVersionComparer _comparer = DefaultModVersionComparer.Instance;

    private static readonly ModVersionImportCandidate _unambiguous =
        new(Mod("FS25_Plain"), Vs("1.0.0.0"), Vs("1.1.0.0"));

    private static readonly ModVersionImportCandidate _ambiguous =
        new(Mod("FS25_Awkward"), Vs("1.0"), Vs("v1"));


    [Fact]
    public void A_mod_the_comparer_settled_is_ready_without_waiting_on_the_ambiguous_ones()
    {
        var plan = ModVersionImportPlanner.Plan([_unambiguous, _ambiguous], _comparer);

        Assert.Equal(Mod("FS25_Plain"), Assert.Single(plan.Ready).ModId);
        Assert.Equal(Mod("FS25_Awkward"), Assert.Single(plan.Arbitration).ModId);
        Assert.True(plan.NeedsArbitration);
    }

    [Fact]
    public void An_import_with_nothing_ambiguous_asks_nothing()
    {
        var plan = ModVersionImportPlanner.Plan([_unambiguous], _comparer);

        Assert.False(plan.NeedsArbitration);
        Assert.Empty(plan.Arbitration);
        Assert.Empty(plan.ModIdsSkippedByCancelling);
    }

    [Fact]
    public void Cancelling_arbitration_costs_only_the_mods_it_was_asking_about()
    {
        var plan = ModVersionImportPlanner.Plan(
            [_unambiguous, _ambiguous, _unambiguous with { ModId = Mod("FS25_Other") }],
            _comparer);

        Assert.Equal([Mod("FS25_Awkward")], plan.ModIdsSkippedByCancelling);
        Assert.Equal([Mod("FS25_Plain"), Mod("FS25_Other")], plan.Ready.Select(x => x.ModId));
    }

    [Fact]
    public void An_arbitration_item_lists_the_versions_in_the_order_that_was_derived()
    {
        var candidate = new ModVersionImportCandidate(Mod("FS25_Awkward"), Vs("1.0", "2.0"), Vs("v1", "1.5"));

        var item = Assert.Single(ModVersionImportPlanner.Plan([candidate], _comparer).Arbitration);

        Assert.Equal(Vs("1.0", "v1", "1.5", "2.0"), item.Versions.Select(x => x.VersionId));
    }

    [Fact]
    public void An_arbitration_item_marks_the_versions_nothing_could_place()
    {
        var candidate = new ModVersionImportCandidate(Mod("FS25_Awkward"), Vs("1.0", "2.0"), Vs("v1", "1.5"));

        var item = Assert.Single(ModVersionImportPlanner.Plan([candidate], _comparer).Arbitration);

        Assert.Equal(Vs("v1"), item.Versions.Where(x => x.IsUnplaceable).Select(x => x.VersionId));
        Assert.Equal(new ModVersionPair(V("1.0"), V("v1")), Assert.Single(item.UnorderedPairs));
    }

    [Fact]
    public void An_arbitration_item_separates_the_incoming_versions_from_the_registered_ones()
    {
        var candidate = new ModVersionImportCandidate(Mod("FS25_Awkward"), Vs("1.0", "2.0"), Vs("v1", "1.5"));

        var item = Assert.Single(ModVersionImportPlanner.Plan([candidate], _comparer).Arbitration);

        Assert.Equal(Vs("1.0", "2.0"), item.RegisteredInOrder);
        Assert.Equal(Vs("v1", "1.5"), item.Incoming);
    }

    [Fact]
    public void An_arbitrated_item_turns_back_into_placements()
    {
        var candidate = new ModVersionImportCandidate(Mod("FS25_Awkward"), Vs("1.0", "2.0"), Vs("v1", "1.5"));
        var item = Assert.Single(ModVersionImportPlanner.Plan([candidate], _comparer).Arbitration);

        var resolved = ModVersionPlacementPlanner.PlanFor(item.ModId, item.RegisteredInOrder, item.Incoming, Vs("v1", "1.0", "1.5", "2.0"));

        Assert.False(resolved.NeedsArbitration);
        Assert.Equal(
            [
                new ModVersionRegistration(V("v1"), new ModVersionPlacement(null, V("1.0"))),
                new ModVersionRegistration(V("1.5"), new ModVersionPlacement(V("1.0"), V("2.0")))
            ],
            resolved.Registrations);
    }

    [Fact]
    public void Planning_nothing_is_not_an_error()
    {
        var plan = ModVersionImportPlanner.Plan([], _comparer);

        Assert.Empty(plan.Ready);
        Assert.Empty(plan.Arbitration);
    }
}
