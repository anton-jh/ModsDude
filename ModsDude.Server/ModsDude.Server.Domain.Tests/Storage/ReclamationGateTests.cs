using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Domain.Tests.Storage;

/// <summary>
/// The gate decides whether a sweep may delete anything. What it must never allow is a sweep measured
/// against a database that is not storage's own, to which every blob looks like garbage.
/// </summary>
public class ReclamationGateTests
{
    private const double _maxShare = 0.5;

    private static readonly Guid _ours = Guid.NewGuid();
    private static readonly Guid _theirs = Guid.NewGuid();


    [Fact]
    public void A_bound_container_is_swept_however_much_it_loses()
    {
        var decision = ReclamationGate.Decide([Container("mods", Plan(reclaimable: 100, scanned: 100), _ours)], _ours, _maxShare);

        Assert.True(decision.MayProceed);
        Assert.Empty(decision.ToMark);
        Assert.False(decision.CreateBinding);
    }

    [Fact]
    public void A_container_bound_to_another_database_stops_the_whole_sweep()
    {
        var decision = ReclamationGate.Decide(
            [Container("mods", Plan(0, 10), _ours), Container("savegames", Plan(0, 10), _theirs)],
            _ours,
            _maxShare);

        Assert.False(decision.MayProceed);
        Assert.Single(decision.Refusals);
    }

    [Fact]
    public void A_marked_container_measured_against_a_database_with_no_binding_is_refused()
    {
        // A reset database, pointed at storage that an earlier one bound.
        var decision = ReclamationGate.Decide([Container("mods", Plan(0, 10), _theirs)], null, _maxShare);

        Assert.False(decision.MayProceed);
    }

    [Fact]
    public void A_first_sweep_binds_and_marks_every_container()
    {
        var decision = ReclamationGate.Decide(
            [Container("mods", Plan(1, 100), null), Container("savegames", Plan(0, 0), null)],
            null,
            _maxShare);

        Assert.True(decision.MayProceed);
        Assert.True(decision.CreateBinding);
        Assert.Equal(["mods", "savegames"], decision.ToMark);
    }

    [Fact]
    public void An_unbound_container_that_would_lose_most_of_itself_is_refused()
    {
        var decision = ReclamationGate.Decide([Container("mods", Plan(80, 100), null)], null, _maxShare);

        Assert.False(decision.MayProceed);
        Assert.False(decision.CreateBinding);
        Assert.Empty(decision.ToMark);
    }

    [Fact]
    public void A_container_added_after_binding_is_marked_without_a_new_binding()
    {
        var decision = ReclamationGate.Decide(
            [Container("mods", Plan(0, 10), _ours), Container("mod-images", Plan(0, 10), null)],
            _ours,
            _maxShare);

        Assert.True(decision.MayProceed);
        Assert.False(decision.CreateBinding);
        Assert.Equal(["mod-images"], decision.ToMark);
    }


    private static ContainerReclamation Container(string name, ReclamationPlan plan, Guid? marker) => new(name, plan, marker);

    private static ReclamationPlan Plan(int reclaimable, int scanned)
    {
        var blobs = Enumerable.Range(0, reclaimable).Select(x => new StoredBlob($"blob-{x}", DateTimeOffset.MinValue)).ToList();

        return new ReclamationPlan(blobs, [], [], scanned);
    }
}
