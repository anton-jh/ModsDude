using ModsDude.Server.Domain.ModHub;

namespace ModsDude.Server.Domain.Tests.ModHub;

public class ModHubListingChangesTests
{
    private static readonly int[] _head = [10, 11, 12, 13, 14, 15];


    [Fact]
    public void An_unchanged_listing_has_nothing_above_the_boundary()
    {
        Assert.Equal(0, ModHubListingChanges.FindBoundary([10, 11, 12, 13, 14, 15, 16], _head));
    }

    [Fact]
    public void New_mods_are_everything_above_the_previous_head()
    {
        Assert.Equal(2, ModHubListingChanges.FindBoundary([1, 2, 10, 11, 12, 13], _head));
    }

    [Fact]
    public void A_mod_lifted_out_of_the_previous_head_is_above_the_boundary()
    {
        // 13 was updated: it leaves its place and comes back at the top.
        Assert.Equal(1, ModHubListingChanges.FindBoundary([13, 10, 11, 12, 14, 15], _head));
    }

    [Fact]
    public void A_mod_lifted_from_below_the_previous_head_is_above_the_boundary()
    {
        Assert.Equal(1, ModHubListingChanges.FindBoundary([99, 10, 11, 12, 13, 14], _head));
    }

    [Fact]
    public void Several_updates_out_of_the_previous_head_are_all_above_the_boundary()
    {
        // 13 updated, then 11 - the most recent first.
        Assert.Equal(2, ModHubListingChanges.FindBoundary([11, 13, 10, 12, 14, 15], _head));
    }

    [Fact]
    public void A_mod_removed_from_the_previous_head_does_not_hide_the_boundary()
    {
        // 11 is gone from ModHub; the listing closes the gap.
        Assert.Equal(1, ModHubListingChanges.FindBoundary([5, 10, 12, 13, 14, 15], _head));
    }

    [Fact]
    public void A_boundary_not_yet_read_asks_for_another_page()
    {
        Assert.Null(ModHubListingChanges.FindBoundary([1, 2, 3, 10], _head));
    }

    [Fact]
    public void A_listing_that_never_resumes_the_previous_order_has_no_boundary()
    {
        Assert.Null(ModHubListingChanges.FindBoundary([1, 2, 3, 4, 5, 6, 7, 8], _head));
    }

    [Fact]
    public void Near_the_end_of_the_previous_head_fewer_confirmations_are_needed()
    {
        // Everything but 15 changed; what follows 15 was never in the head, so 15 alone confirms.
        Assert.Equal(5, ModHubListingChanges.FindBoundary([14, 13, 12, 11, 10, 15, 16, 17], _head));
    }

    [Fact]
    public void No_previous_head_has_no_boundary()
    {
        Assert.Null(ModHubListingChanges.FindBoundary([1, 2, 3], []));
    }

    [Fact]
    public void An_update_while_already_first_is_not_visible()
    {
        // Documented rather than wanted: the stalest-first refresh is what catches this.
        Assert.Equal(0, ModHubListingChanges.FindBoundary([10, 11, 12, 13, 14, 15], _head));
    }
}
