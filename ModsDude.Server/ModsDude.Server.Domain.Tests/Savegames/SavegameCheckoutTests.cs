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


    /// <summary>
    /// A claim used to lapse a day after it was last renewed and read as stale. The day was arbitrary, so
    /// the date it produced meant nothing to whoever read it: a claim is held until it ends, and what a
    /// reader is told is when it was taken.
    /// </summary>
    [Fact]
    public void A_fresh_claim_is_held_and_open()
    {
        var checkout = CreateCheckout();

        Assert.Equal(SavegameCheckoutStatus.Held, checkout.Status);
        Assert.True(checkout.IsOpen);
    }

    [Fact]
    public void An_ended_claim_reads_as_ended()
    {
        var checkout = CreateCheckout();

        checkout.End(_takenAt.AddHours(1), SavegameCheckoutEndReason.CheckedIn);

        Assert.Equal(SavegameCheckoutStatus.Ended, checkout.Status);
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
    /// A discard ends the claim without minting a snapshot - taken by mistake, never played. Without
    /// it the only ways out are a junk snapshot or waiting to be taken over.
    /// </summary>
    [Fact]
    public void A_claim_can_end_without_anything_having_been_played()
    {
        var checkout = CreateCheckout();

        checkout.End(_takenAt.AddMinutes(1), SavegameCheckoutEndReason.Discarded);

        Assert.Equal(SavegameCheckoutStatus.Ended, checkout.Status);
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
