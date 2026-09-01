using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Invites;

public class RepoInviteTests
{
    private static readonly DateTime _created = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly RepoId _repo = new(Guid.NewGuid());
    private static readonly UserId _creator = new("creator");


    [Fact]
    public void A_fresh_invite_with_no_limits_is_active()
    {
        var invite = CreateInvite();

        Assert.Equal(InviteStatus.Active, invite.GetStatus(_created.AddYears(10)));
    }

    [Fact]
    public void Redeeming_counts_a_join()
    {
        var invite = CreateInvite();

        invite.Redeem(_created);
        invite.Redeem(_created);

        Assert.Equal(2, invite.Uses);
    }

    [Fact]
    public void An_invite_is_exhausted_once_its_joins_are_used_up()
    {
        var invite = CreateInvite(maximumUses: 2);

        invite.Redeem(_created);
        Assert.Equal(InviteStatus.Active, invite.GetStatus(_created));

        invite.Redeem(_created);
        Assert.Equal(InviteStatus.Exhausted, invite.GetStatus(_created));
    }

    [Fact]
    public void An_invite_expires_at_the_moment_it_says()
    {
        var expiry = _created.AddHours(1);
        var invite = CreateInvite(expiresAt: expiry);

        Assert.Equal(InviteStatus.Active, invite.GetStatus(expiry.AddSeconds(-1)));
        Assert.Equal(InviteStatus.Expired, invite.GetStatus(expiry));
    }

    [Fact]
    public void Revoking_reports_ahead_of_expiry_and_exhaustion()
    {
        var invite = CreateInvite(maximumUses: 1, expiresAt: _created.AddHours(1));

        invite.Redeem(_created);
        invite.Revoke();

        Assert.Equal(InviteStatus.Revoked, invite.GetStatus(_created.AddDays(1)));
    }

    [Fact]
    public void An_invite_that_is_not_active_cannot_be_redeemed()
    {
        var invite = CreateInvite(maximumUses: 1);

        invite.Redeem(_created);

        Assert.Throws<InvalidOperationException>(() => invite.Redeem(_created));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_invite_cannot_be_limited_to_no_joins(int maximumUses)
    {
        Assert.Throws<DomainValidationException>(() => CreateInvite(maximumUses: maximumUses));
    }

    [Fact]
    public void An_invite_cannot_grant_admin()
    {
        Assert.Throws<DomainValidationException>(
            () => new RepoInvite(_repo, InviteCodes.Generate(), RepoMembershipLevel.Admin, _creator, _created, null, null));
    }

    [Fact]
    public void An_invite_cannot_expire_before_it_is_created()
    {
        Assert.Throws<DomainValidationException>(() => CreateInvite(expiresAt: _created.AddHours(-1)));
    }


    private static RepoInvite CreateInvite(int? maximumUses = null, DateTime? expiresAt = null)
    {
        return new RepoInvite(
            _repo,
            InviteCodes.Generate(),
            RepoMembershipLevel.Member,
            _creator,
            _created,
            expiresAt,
            maximumUses);
    }
}
