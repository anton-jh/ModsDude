using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModVersions;
using static ModsDude.Client.Core.Tests.Keys;

namespace ModsDude.Client.Core.Tests.ModVersions;

public class ModVersionPlacementPlannerTests
{
    private static readonly ModKey _modId = Mod("FS25_TestMod");

    private static readonly DefaultModVersionComparer _comparer = DefaultModVersionComparer.Instance;


    [Fact]
    public void The_first_version_of_a_mod_asserts_an_empty_set()
    {
        var plan = ModVersionPlacementPlanner.Plan(_modId, [], Vs("1.0"), _comparer);

        var registration = Assert.Single(plan.Registrations);
        Assert.Equal(V("1.0"), registration.VersionId);
        Assert.Null(registration.Placement.After);
        Assert.Null(registration.Placement.Before);
    }

    [Fact]
    public void A_version_newer_than_everything_registered_is_appended_after_the_last_one()
    {
        var plan = ModVersionPlacementPlanner.Plan(_modId, Vs("1.0", "1.1"), Vs("1.2"), _comparer);

        var registration = Assert.Single(plan.Registrations);
        Assert.Equal(new ModVersionPlacement(V("1.1"), null), registration.Placement);
    }

    [Fact]
    public void A_version_older_than_everything_registered_asserts_the_oldest_one()
    {
        var plan = ModVersionPlacementPlanner.Plan(_modId, Vs("1.1", "1.2"), Vs("1.0"), _comparer);

        var registration = Assert.Single(plan.Registrations);
        Assert.Equal(new ModVersionPlacement(null, V("1.1")), registration.Placement);
    }

    [Fact]
    public void A_back_filled_version_names_both_the_neighbours_it_goes_between()
    {
        var plan = ModVersionPlacementPlanner.Plan(_modId, Vs("1.0", "1.2"), Vs("1.1"), _comparer);

        var registration = Assert.Single(plan.Registrations);
        Assert.Equal(new ModVersionPlacement(V("1.0"), V("1.2")), registration.Placement);
    }

    [Fact]
    public void A_version_that_is_already_registered_is_not_placed_again()
    {
        var plan = ModVersionPlacementPlanner.Plan(_modId, Vs("1.0", "1.1"), Vs("1.1", "1.2"), _comparer);

        Assert.Equal(V("1.2"), Assert.Single(plan.Registrations).VersionId);
        Assert.Equal(Vs("1.0", "1.1", "1.2"), plan.Order);
    }


    /// <summary>
    /// The worked example from docs/09-mod-catalog.md: v1 and v4 registered, v2 found in a mod
    /// folder and v3 in Downloads. The intended result is v1, v2, v3, v4, which means v4 moves.
    /// </summary>
    [Fact]
    public void Several_new_versions_of_one_mod_are_each_placed_against_the_final_order()
    {
        var registered = Vs("1.0.0.0", "4.0.0.0");

        var plan = ModVersionPlacementPlanner.Plan(_modId, registered, Vs("3.0.0.0", "2.0.0.0"), _comparer);

        Assert.Equal(Vs("1.0.0.0", "2.0.0.0", "3.0.0.0", "4.0.0.0"), plan.Order);
        Assert.Equal(
            [
                new ModVersionRegistration(V("2.0.0.0"), new ModVersionPlacement(V("1.0.0.0"), V("4.0.0.0"))),
                new ModVersionRegistration(V("3.0.0.0"), new ModVersionPlacement(V("2.0.0.0"), V("4.0.0.0")))
            ],
            plan.Registrations);
    }

    [Fact]
    public void Every_step_is_valid_against_the_ordering_the_previous_one_left()
    {
        var registered = Vs("1.0.0.0", "4.0.0.0");

        var plan = ModVersionPlacementPlanner.Plan(_modId, registered, Vs("3.0.0.0", "2.0.0.0"), _comparer);

        // Applying each placement in turn, rejecting any whose neighbours are no longer adjacent -
        // which is what the server does - has to arrive at the intended order.
        Assert.Equal(plan.Order, Apply(registered, plan.Registrations));
    }

    [Fact]
    public void Inserting_ahead_of_a_registered_version_moves_that_version()
    {
        var registered = Vs("1.0.0.0", "4.0.0.0");

        var plan = ModVersionPlacementPlanner.Plan(_modId, registered, Vs("2.0.0.0", "3.0.0.0"), _comparer);

        var applied = Apply(registered, plan.Registrations);

        Assert.Equal(1, Array.IndexOf(registered, V("4.0.0.0")));
        Assert.Equal(3, applied.IndexOf(V("4.0.0.0")));
    }

    [Fact]
    public void A_run_of_new_versions_around_the_registered_ones_stays_valid_throughout()
    {
        var registered = Vs("2.0", "5.0");

        var plan = ModVersionPlacementPlanner.Plan(_modId, registered, Vs("1.0", "3.0", "4.0", "6.0"), _comparer);

        Assert.Equal(Vs("1.0", "2.0", "3.0", "4.0", "5.0", "6.0"), Apply(registered, plan.Registrations));
    }

    [Fact]
    public void Placements_are_emitted_oldest_first()
    {
        var plan = ModVersionPlacementPlanner.Plan(_modId, Vs("2.0"), Vs("3.0", "1.0"), _comparer);

        Assert.Equal(Vs("1.0", "3.0"), plan.Registrations.Select(x => x.VersionId));
    }

    [Fact]
    public void The_stored_order_of_registered_versions_is_never_re_derived()
    {
        // 1.0 against 1.0.0 is undecidable, but the repo has already settled it and nobody should
        // be asked a second time.
        var plan = ModVersionPlacementPlanner.Plan(_modId, Vs("1.0.0", "1.0"), Vs("2.0"), _comparer);

        Assert.False(plan.NeedsArbitration);
        Assert.Equal(Vs("1.0.0", "1.0", "2.0"), plan.Order);
    }


    [Fact]
    public void A_mod_whose_ordering_needs_arbitration_produces_no_placements_at_all()
    {
        var plan = ModVersionPlacementPlanner.Plan(_modId, Vs("1.0"), Vs("v1"), _comparer);

        Assert.True(plan.NeedsArbitration);
        Assert.Empty(plan.Registrations);
        Assert.Equal(new ModVersionPair(V("1.0"), V("v1")), Assert.Single(plan.UnorderedPairs));
    }

    [Fact]
    public void An_arbitrated_order_produces_the_placements_the_comparer_could_not()
    {
        var plan = ModVersionPlacementPlanner.PlanFor(_modId, Vs("1.0", "2.0"), Vs("v1"), Vs("v1", "1.0", "2.0"));

        Assert.False(plan.NeedsArbitration);
        Assert.Equal(new ModVersionPlacement(null, V("1.0")), Assert.Single(plan.Registrations).Placement);
        Assert.Equal(Vs("v1", "1.0", "2.0"), Apply(Vs("1.0", "2.0"), plan.Registrations));
    }

    [Fact]
    public void An_arbitrated_order_that_reorders_registered_versions_is_rejected()
    {
        // Placements only insert, so nothing the dialog produces can move one registered version
        // past another. Reordering those is the Manage page's job.
        Assert.Throws<ArgumentException>(
            () => ModVersionPlacementPlanner.PlanFor(_modId, Vs("1.0", "2.0"), Vs("v1"), Vs("v1", "2.0", "1.0")));
    }

    [Fact]
    public void An_arbitrated_order_missing_a_version_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => ModVersionPlacementPlanner.PlanFor(_modId, Vs("1.0", "2.0"), Vs("v1"), Vs("1.0", "2.0")));
    }

    [Fact]
    public void An_arbitrated_order_carrying_a_version_nobody_asked_about_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => ModVersionPlacementPlanner.PlanFor(_modId, Vs("1.0", "2.0"), Vs("v1"), Vs("1.0", "v1", "2.0", "3.0")));
    }

    [Fact]
    public void An_arbitrated_order_listing_a_version_twice_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => ModVersionPlacementPlanner.PlanFor(_modId, Vs("1.0", "2.0"), Vs("v1"), Vs("v1", "1.0", "1.0", "2.0")));
    }


    /// <summary>
    /// Replays the placements the way the server does, asserting the same thing
    /// <c>ModVersionSequencer.CheckPlacementIsValid</c> asserts: the two named neighbours have to
    /// still be adjacent when the placement lands.
    /// </summary>
    private static List<ModVersionKey> Apply(IReadOnlyList<ModVersionKey> registered, IReadOnlyList<ModVersionRegistration> registrations)
    {
        var order = new List<ModVersionKey>(registered);

        foreach (var registration in registrations)
        {
            var after = registration.Placement.After;
            var before = registration.Placement.Before;

            var afterPosition = after is null ? -1 : order.IndexOf(after.Value);
            var beforePosition = before is null ? order.Count : order.IndexOf(before.Value);

            Assert.True(after is null || afterPosition >= 0, $"'{after}' is not registered");
            Assert.True(before is null || beforePosition >= 0, $"'{before}' is not registered");
            Assert.True(
                beforePosition == afterPosition + 1,
                $"'{after}' and '{before}' are no longer adjacent when placing '{registration.VersionId}'");

            order.Insert(beforePosition, registration.VersionId);
        }

        return order;
    }
}
