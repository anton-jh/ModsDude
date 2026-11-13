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
    private const RepoMembershipLevel CreatorLevel = RepoMembershipLevel.Member;


    [Fact]
    public void A_fresh_invite_with_no_limits_is_active()
    {
        var invite = CreateInvite();

        Assert.Equal(InviteStatus.Active, invite.GetStatus(_created.AddYears(10), CreatorLevel));
    }

    [Fact]
    public void An_invite_created_on_the_admin_pages_has_no_creator_and_still_works()
    {
        var invite = new RepoInvite(_repo, InviteCodes.Generate(), new RepoInviteRequestId(Guid.NewGuid()), RepoMembershipLevel.Guest, createdBy: null, _created, null, null);

        invite.Redeem(_created, CreatorLevel);

        Assert.Null(invite.CreatedBy);
        Assert.Equal(1, invite.Uses);
    }

    [Fact]
    public void Redeeming_counts_a_join()
    {
        var invite = CreateInvite();

        invite.Redeem(_created, CreatorLevel);
        invite.Redeem(_created, CreatorLevel);

        Assert.Equal(2, invite.Uses);
    }

    [Fact]
    public void An_invite_is_exhausted_once_its_joins_are_used_up()
    {
        var invite = CreateInvite(maximumUses: 2);

        invite.Redeem(_created, CreatorLevel);
        Assert.Equal(InviteStatus.Active, invite.GetStatus(_created, CreatorLevel));

        invite.Redeem(_created, CreatorLevel);
        Assert.Equal(InviteStatus.Exhausted, invite.GetStatus(_created, CreatorLevel));
    }

    [Fact]
    public void An_invite_expires_at_the_moment_it_says()
    {
        var expiry = _created.AddHours(1);
        var invite = CreateInvite(expiresAt: expiry);

        Assert.Equal(InviteStatus.Active, invite.GetStatus(expiry.AddSeconds(-1), CreatorLevel));
        Assert.Equal(InviteStatus.Expired, invite.GetStatus(expiry, CreatorLevel));
    }

    [Fact]
    public void Revoking_reports_ahead_of_expiry_and_exhaustion()
    {
        var invite = CreateInvite(maximumUses: 1, expiresAt: _created.AddHours(1));

        invite.Redeem(_created, CreatorLevel);
        invite.Revoke(_created);

        Assert.Equal(InviteStatus.Revoked, invite.GetStatus(_created.AddDays(1), CreatorLevel));
    }

    /// <summary>
    /// Two gestures in one: somebody switching a code off is done with it, and leaving it on the
    /// list afterwards means every retired code accumulates there forever.
    /// </summary>
    [Fact]
    public void Revoking_also_takes_the_invite_off_the_list()
    {
        var invite = CreateInvite();

        invite.Revoke(_created);

        Assert.Equal(_created, invite.DismissedAt);
    }

    [Fact]
    public void A_fresh_invite_is_on_the_list()
    {
        Assert.Null(CreateInvite().DismissedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_dead_invite_can_be_dismissed(bool byExhaustion)
    {
        var invite = byExhaustion
            ? CreateInvite(maximumUses: 1)
            : CreateInvite(expiresAt: _created.AddHours(1));

        if (byExhaustion)
        {
            invite.Redeem(_created, CreatorLevel);
        }

        var later = _created.AddDays(1);

        invite.Dismiss(later);

        Assert.Equal(later, invite.DismissedAt);

        // Dismissing hides it; it does not rewrite what happened to it.
        Assert.False(invite.IsRevoked);
    }

    /// <summary>
    /// The one state this must never produce: a code that still works, still out in the world, and
    /// off the only screen from which it could be revoked.
    /// </summary>
    [Fact]
    public void An_active_invite_cannot_be_dismissed()
    {
        var invite = CreateInvite();

        Assert.Throws<DomainValidationException>(() => invite.Dismiss(_created));

        Assert.Null(invite.DismissedAt);
    }

    [Fact]
    public void An_invite_that_is_not_active_cannot_be_redeemed()
    {
        var invite = CreateInvite(maximumUses: 1);

        invite.Redeem(_created, CreatorLevel);

        Assert.Throws<InvalidOperationException>(() => invite.Redeem(_created, CreatorLevel));
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
            () => new RepoInvite(_repo, InviteCodes.Generate(), new RepoInviteRequestId(Guid.NewGuid()), RepoMembershipLevel.Admin, _creator, _created, null, null));
    }

    [Fact]
    public void An_invite_cannot_expire_before_it_is_created()
    {
        Assert.Throws<DomainValidationException>(() => CreateInvite(expiresAt: _created.AddHours(-1)));
    }


    [Theory]
    [InlineData(null)]
    [InlineData(RepoMembershipLevel.Guest)]
    public void An_invite_whose_inviter_left_or_dropped_below_member_stops_working(RepoMembershipLevel? inviterLevel)
    {
        var invite = CreateInvite(level: RepoMembershipLevel.Guest);

        Assert.Equal(InviteStatus.InviterLostAccess, invite.GetStatus(_created, inviterLevel));
        Assert.Throws<InvalidOperationException>(() => invite.Redeem(_created, inviterLevel));
        Assert.Equal(0, invite.Uses);
    }

    [Fact]
    public void An_invite_stops_working_when_its_inviter_drops_below_the_level_it_grants()
    {
        var invite = CreateInvite(level: RepoMembershipLevel.Member);

        Assert.Equal(InviteStatus.Active, invite.GetStatus(_created, RepoMembershipLevel.Member));
        Assert.Equal(InviteStatus.InviterLostAccess, invite.GetStatus(_created, RepoMembershipLevel.Guest));
    }

    [Fact]
    public void An_invite_works_again_when_its_inviter_regains_the_access()
    {
        var invite = CreateInvite();

        Assert.Equal(InviteStatus.InviterLostAccess, invite.GetStatus(_created, null));

        invite.Redeem(_created, RepoMembershipLevel.Admin);

        Assert.Equal(1, invite.Uses);
    }

    [Fact]
    public void An_invite_made_on_the_admin_pages_does_not_depend_on_anybody_in_the_repo()
    {
        var invite = new RepoInvite(_repo, InviteCodes.Generate(), new RepoInviteRequestId(Guid.NewGuid()), RepoMembershipLevel.Member, createdBy: null, _created, null, null);

        Assert.Equal(InviteStatus.Active, invite.GetStatus(_created, null));
    }

    [Fact]
    public void The_invites_own_limits_are_reported_ahead_of_its_inviters_access()
    {
        var invite = CreateInvite(expiresAt: _created.AddHours(1));

        Assert.Equal(InviteStatus.Expired, invite.GetStatus(_created.AddHours(2), null));
    }

    /// <summary>Its inviter's access can come back, so hiding it could hide a code that works again later.</summary>
    [Fact]
    public void An_invite_stopped_only_by_its_inviters_access_cannot_be_dismissed()
    {
        var invite = CreateInvite();

        Assert.Throws<DomainValidationException>(() => invite.Dismiss(_created));
        Assert.Equal(InviteStatus.Active, invite.GetOwnStatus(_created));
    }


    private static RepoInvite CreateInvite(int? maximumUses = null, DateTime? expiresAt = null, RepoMembershipLevel level = RepoMembershipLevel.Member)
    {
        return new RepoInvite(
            _repo,
            InviteCodes.Generate(),
            new RepoInviteRequestId(Guid.NewGuid()),
            level,
            _creator,
            _created,
            expiresAt,
            maximumUses);
    }

    [Fact]
    public void An_invite_cannot_grant_a_level_that_is_not_defined()
    {
        Assert.Throws<DomainValidationException>(
            () => new RepoInvite(_repo, InviteCodes.Generate(), new RepoInviteRequestId(Guid.NewGuid()), (RepoMembershipLevel)0, _creator, _created, null, null));
    }
}
