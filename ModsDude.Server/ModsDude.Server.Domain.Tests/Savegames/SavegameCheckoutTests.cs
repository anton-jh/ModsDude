using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Savegames;

public class SavegameCheckoutTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly SavegameId _savegameId = new(Guid.NewGuid());
    private static readonly UserId _holder = new("anton");
    private static readonly DateTime _takenAt = new(2026, 3, 3, 20, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void A_fresh_claim_is_held_and_expires_a_lifetime_after_it_was_taken()
    {
        var checkout = CreateCheckout();

        Assert.Equal(_takenAt + SavegameCheckout.Lifetime, checkout.ExpiresAt);
        Assert.Equal(SavegameCheckoutStatus.Held, checkout.GetStatus(_takenAt));
        Assert.True(checkout.IsOpen);
    }

    /// <summary>
    /// The claim stops reading as held at the moment it says, exactly as an invite expires at the
    /// moment it says. A boundary that drifts by a second is a boundary nobody can state.
    /// </summary>
    [Fact]
    public void A_claim_goes_stale_at_the_moment_it_says()
    {
        var checkout = CreateCheckout();

        Assert.Equal(SavegameCheckoutStatus.Held, checkout.GetStatus(checkout.ExpiresAt.AddSeconds(-1)));
        Assert.Equal(SavegameCheckoutStatus.Stale, checkout.GetStatus(checkout.ExpiresAt));
    }

    /// <summary>
    /// The whole point of the type. An expired claim is still somebody's claim: "Anton has had this
    /// since 3 March" must not read as "Anton has this", and it must not read as nobody having it
    /// either - the save is still sitting unchecked-in on his disk. A warning that never clears is a
    /// warning everybody learns to click past, and a warning that silently disappears is worse.
    /// </summary>
    [Fact]
    public void An_expired_claim_that_is_still_open_reads_as_stale_rather_than_ended()
    {
        var checkout = CreateCheckout();

        Assert.Equal(SavegameCheckoutStatus.Stale, checkout.GetStatus(_takenAt.AddDays(30)));
        Assert.True(checkout.IsOpen);
        Assert.Null(checkout.EndedAt);
    }

    /// <summary>
    /// Ended outranks expired because it is the one that actually happened: a claim checked in
    /// yesterday is ended, however long ago it was due to expire.
    /// </summary>
    [Fact]
    public void An_ended_claim_reads_as_ended_however_long_past_its_expiry_it_is()
    {
        var checkout = CreateCheckout();

        checkout.End(_takenAt.AddHours(1), SavegameCheckoutEndReason.CheckedIn);

        Assert.Equal(SavegameCheckoutStatus.Ended, checkout.GetStatus(_takenAt.AddYears(1)));
    }

    /// <summary>
    /// The open row is the current holder - there is no field on the savegame to keep in step with it
    /// - so ending one is the entire mechanism by which a save becomes available again.
    /// </summary>
    [Fact]
    public void Ending_a_claim_records_when_and_why_and_closes_the_row()
    {
        var checkout = CreateCheckout();
        var endedAt = _takenAt.AddHours(2);

        checkout.End(endedAt, SavegameCheckoutEndReason.TakenOver);

        Assert.False(checkout.IsOpen);
        Assert.Equal(endedAt, checkout.EndedAt);
        Assert.Equal(SavegameCheckoutEndReason.TakenOver, checkout.EndedReason);
    }

    [Fact]
    public void Renewing_pushes_the_expiry_out_from_now_rather_than_from_when_it_was_taken()
    {
        var checkout = CreateCheckout();
        var now = _takenAt.AddHours(3);

        checkout.Renew(now);

        Assert.Equal(now + SavegameCheckout.Lifetime, checkout.ExpiresAt);
        Assert.Equal(SavegameCheckoutStatus.Held, checkout.GetStatus(now));
    }

    /// <summary>
    /// Deliberately allowed. The holder coming back after a week is exactly the case renewal exists
    /// for, and refusing it would force somebody to take their own save off themselves before they
    /// could carry on playing it.
    /// </summary>
    [Fact]
    public void A_stale_claim_can_still_be_renewed_by_the_holder_coming_back()
    {
        var checkout = CreateCheckout();
        var now = _takenAt.AddDays(7);

        Assert.Equal(SavegameCheckoutStatus.Stale, checkout.GetStatus(now));

        checkout.Renew(now);

        Assert.Equal(SavegameCheckoutStatus.Held, checkout.GetStatus(now));
    }

    /// <summary>
    /// Renewing an ended claim would resurrect a holder the save has already moved on from, and the
    /// filtered unique index permits one open row - so a second holder would be looking at a claim
    /// that quietly reopened underneath them.
    /// </summary>
    [Fact]
    public void An_ended_claim_cannot_be_renewed()
    {
        var checkout = CreateCheckout();

        checkout.End(_takenAt.AddHours(1), SavegameCheckoutEndReason.CheckedIn);

        Assert.Throws<InvalidOperationException>(() => checkout.Renew(_takenAt.AddHours(2)));
    }

    /// <summary>
    /// Ending twice would overwrite why it ended: a claim checked in and then ended again as a
    /// take-over would end up reading as taken from somebody who had already handed it back.
    /// </summary>
    [Fact]
    public void A_claim_cannot_be_ended_twice()
    {
        var checkout = CreateCheckout();

        checkout.End(_takenAt.AddHours(1), SavegameCheckoutEndReason.CheckedIn);

        Assert.Throws<InvalidOperationException>(
            () => checkout.End(_takenAt.AddHours(2), SavegameCheckoutEndReason.TakenOver));

        Assert.Equal(_takenAt.AddHours(1), checkout.EndedAt);
        Assert.Equal(SavegameCheckoutEndReason.CheckedIn, checkout.EndedReason);
    }

    /// <summary>
    /// A discard ends the claim without minting a version - taken by mistake, never played. Without
    /// it the only ways out are a junk version or waiting to be taken over.
    /// </summary>
    [Fact]
    public void A_claim_can_end_without_anything_having_been_played()
    {
        var checkout = CreateCheckout();

        checkout.End(_takenAt.AddMinutes(1), SavegameCheckoutEndReason.Discarded);

        Assert.Equal(SavegameCheckoutStatus.Ended, checkout.GetStatus(_takenAt.AddMinutes(2)));
        Assert.Equal(SavegameCheckoutEndReason.Discarded, checkout.EndedReason);
    }

    [Fact]
    public void A_claim_records_who_took_it_and_which_savegame_it_is_on()
    {
        var checkout = CreateCheckout();

        Assert.Equal(_repoId, checkout.RepoId);
        Assert.Equal(_savegameId, checkout.SavegameId);
        Assert.Equal(_holder, checkout.UserId);
        Assert.Equal(_takenAt, checkout.TakenAt);
    }


    private static SavegameCheckout CreateCheckout()
        => new(_repoId, _savegameId, _holder, _takenAt);
}
