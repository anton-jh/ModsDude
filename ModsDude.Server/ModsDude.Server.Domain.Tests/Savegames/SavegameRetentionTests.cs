using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Domain.Tests.Savegames;

public class SavegameRetentionTests
{
    [Fact]
    public void A_savegame_with_no_history_has_nothing_to_prune()
    {
        Assert.Empty(SavegameRetention.PlanPrune([], SavegameVersionNumber.None));
    }

    [Fact]
    public void A_history_shorter_than_the_limit_is_kept_whole()
    {
        var plan = SavegameRetention.PlanPrune(Unlabelled(1, 2, 3), Head(3), keep: 10);

        Assert.Empty(plan);
    }

    [Fact]
    public void The_most_recent_versions_are_the_ones_kept()
    {
        var plan = SavegameRetention.PlanPrune(Unlabelled(1, 2, 3, 4, 5), Head(5), keep: 2);

        Assert.Equal([Number(1), Number(2), Number(3)], plan);
    }

    /// <summary>
    /// Oldest first, because that is the order they should be deleted in: a sweep interrupted halfway
    /// has then removed the least interesting ones, rather than a scattering that leaves the history
    /// with holes in the middle of what it kept.
    /// </summary>
    [Fact]
    public void The_plan_is_oldest_first_whatever_order_the_versions_arrived_in()
    {
        var plan = SavegameRetention.PlanPrune(Unlabelled(4, 1, 5, 3, 2), Head(5), keep: 1);

        Assert.Equal([Number(1), Number(2), Number(3), Number(4)], plan);
    }

    /// <summary>
    /// Dropping the current save is not a retention policy, it is data loss. The head is retained
    /// whatever else is true of it.
    /// </summary>
    [Fact]
    public void The_head_is_never_pruned()
    {
        var plan = SavegameRetention.PlanPrune(Unlabelled(1, 2, 3, 4, 5), Head(5), keep: 1);

        Assert.DoesNotContain(Number(5), plan);
    }

    /// <summary>
    /// The head is not always the highest number - a savegame restored onto an older version, or one
    /// whose newest versions were pruned by a policy that has since been widened, has a head inside
    /// its history rather than at the end of it. Retaining the head has to be a rule of its own, not
    /// a side effect of it being the most recent thing.
    /// </summary>
    [Fact]
    public void The_head_is_kept_even_when_recency_alone_would_drop_it()
    {
        var plan = SavegameRetention.PlanPrune(Unlabelled(1, 2, 3, 4, 5), Head(2), keep: 2);

        Assert.Equal([Number(1), Number(3)], plan);
    }

    /// <summary>
    /// Labelling a version is the gesture by which a person says to keep it - a rule small enough to
    /// hold in your head, and one that reuses a field the history already has.
    /// </summary>
    [Fact]
    public void A_labelled_version_is_never_pruned()
    {
        var plan = SavegameRetention.PlanPrune(
            [Unlabelled(1), Labelled(2), Unlabelled(3), Unlabelled(4), Unlabelled(5)],
            Head(5),
            keep: 2);

        Assert.Equal([Number(1), Number(3)], plan);
    }

    /// <summary>
    /// The deliberate half of the rule: labelled versions are <b>exempt</b> from pruning rather than
    /// <b>counted</b> against the limit. Ten kept versions from last spring must not silently push
    /// out the recent ones the policy exists to keep - somebody who labels a lot would otherwise find
    /// that keeping old saves quietly cost them their backups of this week's play.
    /// </summary>
    [Fact]
    public void Labelling_old_versions_does_not_push_recent_ones_out_of_the_kept_set()
    {
        var versions = new[]
        {
            Labelled(1), Labelled(2), Labelled(3), Labelled(4), Labelled(5),
            Labelled(6), Labelled(7), Labelled(8), Labelled(9), Labelled(10),
            Unlabelled(11), Unlabelled(12), Unlabelled(13), Unlabelled(14), Unlabelled(15)
        };

        var plan = SavegameRetention.PlanPrune(versions, Head(15), keep: 5);

        // Under a counted reading the ten labelled ones would consume the whole allowance and
        // versions 11 to 14 would go, which is the opposite of what the limit is for.
        Assert.Empty(plan);
    }

    /// <summary>
    /// Pruning leaves gaps rather than renumbering, so the numbers a plan names are the numbers
    /// somebody read off the history - not positions in a list that has since shifted.
    /// </summary>
    [Fact]
    public void A_history_with_gaps_in_it_is_pruned_by_number_not_by_position()
    {
        var plan = SavegameRetention.PlanPrune(Unlabelled(3, 7, 8, 12, 20), Head(20), keep: 2);

        Assert.Equal([Number(3), Number(7), Number(8)], plan);
    }

    [Fact]
    public void The_default_keeps_ten_versions()
    {
        var versions = Enumerable.Range(1, 12).Select(Unlabelled).ToList();

        var plan = SavegameRetention.PlanPrune(versions, Head(12));

        Assert.Equal(SavegameRetention.DefaultVersionsKept, 12 - plan.Count);
        Assert.Equal([Number(1), Number(2)], plan);
    }

    /// <summary>
    /// A limit of zero would mean pruning everything but the head, which is not a retention policy
    /// anybody meant to configure - far more likely an unset value arriving as a default.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_savegame_cannot_be_kept_to_fewer_than_one_version(int keep)
    {
        Assert.Throws<DomainValidationException>(
            () => SavegameRetention.PlanPrune(Unlabelled(1, 2, 3), Head(3), keep));
    }


    /// <summary>
    /// The recency window counts unlabelled versions only. A labelled one is kept whatever happens,
    /// so letting it take a place in the window would mean somebody who named their last two saves
    /// silently ends up with two backups where the policy promised ten - the keeping gesture causing
    /// the loss. Versions 4 and 5 are named here, so the two the window is for are 3 and 2.
    /// </summary>
    [Fact]
    public void A_recent_label_does_not_consume_the_recency_window()
    {
        var plan = SavegameRetention.PlanPrune(
            [Unlabelled(1), Unlabelled(2), Unlabelled(3), Labelled(4), Labelled(5)],
            Head(5),
            keep: 2);

        Assert.Equal([Number(1)], plan);
    }


    private static SavegameVersionNumber Number(int value) => new(value);
    private static SavegameVersionNumber Head(int value) => new(value);

    private static SavegameVersionRetention Unlabelled(int number) => new(Number(number), false);
    private static SavegameVersionRetention Labelled(int number) => new(Number(number), true);

    private static IReadOnlyList<SavegameVersionRetention> Unlabelled(params int[] numbers)
        => [.. numbers.Select(Unlabelled)];
}
