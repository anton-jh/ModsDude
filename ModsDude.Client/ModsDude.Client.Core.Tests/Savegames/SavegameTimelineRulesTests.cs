using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Core.Tests.Savegames;

public class SavegameTimelineRulesTests
{
    private static readonly DateTime _evening = new(2026, 3, 3, 19, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void A_check_in_that_minted_a_snapshot_and_ended_its_claim_gets_no_ending_row()
    {
        var claim = Claim(endedAt: _evening.AddHours(2));

        Assert.Empty(SavegameTimelineRules.EndingsToShow([Snapshot(claim.Id, _evening.AddHours(2))], [claim]));
    }

    /// <summary>
    /// Keeping playing stamps the claim on a snapshot without ending it, so a later check-in that
    /// changed nothing is the only thing saying the claim ended.
    /// </summary>
    [Fact]
    public void An_ending_after_a_check_in_that_kept_playing_gets_its_own_row()
    {
        var claim = Claim(endedAt: _evening.AddHours(3));

        var shown = SavegameTimelineRules.EndingsToShow([Snapshot(claim.Id, _evening.AddHours(1))], [claim]);

        Assert.Same(claim, Assert.Single(shown));
    }

    [Fact]
    public void A_check_in_that_kept_playing_and_a_later_one_that_minted_show_no_ending_row()
    {
        var claim = Claim(endedAt: _evening.AddHours(3));

        Assert.Empty(SavegameTimelineRules.EndingsToShow(
            [Snapshot(claim.Id, _evening.AddHours(1)), Snapshot(claim.Id, _evening.AddHours(3))],
            [claim]));
    }

    /// <summary>A discard, a take-over, or a check-in whose snapshot has been pruned or is not loaded.</summary>
    [Fact]
    public void An_ending_no_snapshot_records_gets_its_own_row()
    {
        var claim = Claim(endedAt: _evening.AddHours(2));

        Assert.Same(claim, Assert.Single(SavegameTimelineRules.EndingsToShow([], [claim])));
    }

    [Fact]
    public void An_open_claim_has_no_ending()
    {
        var claim = Claim(endedAt: null);

        Assert.Empty(SavegameTimelineRules.EndingsToShow([Snapshot(claim.Id, _evening.AddHours(1))], [claim]));
    }

    [Fact]
    public void A_snapshot_of_another_claim_at_the_same_moment_does_not_hide_this_ending()
    {
        var claim = Claim(endedAt: _evening.AddHours(2));

        Assert.Single(SavegameTimelineRules.EndingsToShow([Snapshot(Guid.NewGuid(), _evening.AddHours(2))], [claim]));
    }


    private static SavegameCheckoutDto Claim(DateTime? endedAt) => new()
    {
        Id = Guid.NewGuid(),
        TakenAt = _evening,
        EndedAt = endedAt,
        EndedReason = endedAt is null ? null : SavegameCheckoutEndReason.CheckedIn,
        Status = endedAt is null ? SavegameCheckoutStatus.Held : SavegameCheckoutStatus.Ended
    };

    private static SavegameSnapshotDto Snapshot(Guid claimId, DateTime created) => new()
    {
        CheckoutId = claimId,
        Created = created
    };
}
