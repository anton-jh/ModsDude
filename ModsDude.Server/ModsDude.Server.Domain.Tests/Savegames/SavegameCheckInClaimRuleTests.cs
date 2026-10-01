using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Savegames;

public class SavegameCheckInClaimRuleTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly SavegameId _savegameId = new(Guid.NewGuid());
    private static readonly UserId _caller = new("anton");
    private static readonly UserId _other = new("friend");
    private static readonly DateTime _takenAt = new(2026, 3, 3, 20, 0, 0, DateTimeKind.Utc);


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Handing_back_ends_the_callers_own_claim_whatever_take_over_says(bool takeOver)
    {
        Assert.Equal(SavegameCheckInClaim.EndsCallers, SavegameCheckInClaimRule.Decide(HeldBy(_caller), _caller, keepPlaying: false, takeOver));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keeping_playing_keeps_the_callers_own_claim(bool takeOver)
    {
        Assert.Equal(SavegameCheckInClaim.KeepsCallers, SavegameCheckInClaimRule.Decide(HeldBy(_caller), _caller, keepPlaying: true, takeOver));
    }

    /// <summary>
    /// A forced check-in by somebody who never had the save ends nobody's claim: the new head is what
    /// tells the holder they were overtaken.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Handing_back_leaves_somebody_elses_claim_alone(bool takeOver)
    {
        Assert.Equal(SavegameCheckInClaim.LeavesAlone, SavegameCheckInClaimRule.Decide(HeldBy(_other), _caller, keepPlaying: false, takeOver));
    }

    [Fact]
    public void Handing_back_a_savegame_nobody_holds_changes_no_claim()
    {
        Assert.Equal(SavegameCheckInClaim.LeavesAlone, SavegameCheckInClaimRule.Decide(null, _caller, keepPlaying: false, takeOver: false));
    }

    [Fact]
    public void Keeping_playing_a_savegame_nobody_holds_opens_a_claim_without_asking()
    {
        Assert.Equal(SavegameCheckInClaim.OpensCallers, SavegameCheckInClaimRule.Decide(null, _caller, keepPlaying: true, takeOver: false));
    }

    [Fact]
    public void Keeping_playing_a_savegame_somebody_else_holds_is_refused_until_the_caller_agrees()
    {
        Assert.Equal(SavegameCheckInClaim.RefusedHeldByOther, SavegameCheckInClaimRule.Decide(HeldBy(_other), _caller, keepPlaying: true, takeOver: false));
    }

    [Fact]
    public void Keeping_playing_with_agreement_takes_the_claim_over()
    {
        Assert.Equal(SavegameCheckInClaim.TakesOver, SavegameCheckInClaimRule.Decide(HeldBy(_other), _caller, keepPlaying: true, takeOver: true));
    }

    [Fact]
    public void An_ended_claim_is_not_an_open_one()
    {
        var ended = HeldBy(_caller);
        ended.End(_takenAt.AddHours(1), SavegameCheckoutEndReason.CheckedIn);

        Assert.Throws<DomainValidationException>(() => SavegameCheckInClaimRule.Decide(ended, _caller, keepPlaying: true, takeOver: false));
    }

    [Theory]
    [InlineData(SavegameCheckInClaim.EndsCallers, false, true)]
    [InlineData(SavegameCheckInClaim.KeepsCallers, true, true)]
    [InlineData(SavegameCheckInClaim.LeavesAlone, false, false)]
    [InlineData(SavegameCheckInClaim.OpensCallers, true, false)]
    [InlineData(SavegameCheckInClaim.TakesOver, true, false)]
    public void Who_holds_it_afterwards_and_which_claim_a_snapshot_records(SavegameCheckInClaim claim, bool callerHolds, bool recordsAgainstOpen)
    {
        Assert.Equal(callerHolds, claim.CallerHolds());
        Assert.Equal(recordsAgainstOpen, claim.RecordsAgainstOpenClaim());
    }


    private static SavegameCheckout HeldBy(UserId userId) => new(_repoId, _savegameId, userId, _takenAt);
}
