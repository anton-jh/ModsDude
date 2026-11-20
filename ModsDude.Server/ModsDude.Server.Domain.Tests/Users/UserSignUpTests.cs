using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Users;

public class UserSignUpTests
{
    private static readonly DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly UserId _newUser = new("new-user");

    private readonly User _creator = new(new UserId("creator"), new DisplayName("Creator"), _now);


    [Fact]
    public void Signing_up_without_any_code_is_refused()
    {
        Assert.Throws<DomainValidationException>(() => SignUp(trustCode: null));
    }

    [Fact]
    public void A_trust_code_alone_creates_a_trusted_user()
    {
        var trustCode = CreateTrustCode();

        var user = SignUp(trustCode);

        Assert.True(user.IsTrusted);
        Assert.Equal(_newUser, trustCode.RedeemedBy);
        Assert.Equal(new DisplayName("New"), user.DisplayName);
        Assert.Equal(_now, user.Created);
    }

    [Fact]
    public void Invites_alone_create_an_untrusted_member_of_every_repo()
    {
        var first = CreateRepo();
        var second = CreateRepo();
        var firstInvite = CreateInvite(first, RepoMembershipLevel.Member);
        var secondInvite = CreateInvite(second, RepoMembershipLevel.Guest);

        var user = SignUp(null, new RepoInviteRedemption(first, firstInvite), new RepoInviteRedemption(second, secondInvite));

        Assert.False(user.IsTrusted);
        Assert.Equal(RepoMembershipLevel.Member, first.GetMembership(user.Id)?.Level);
        Assert.Equal(RepoMembershipLevel.Guest, second.GetMembership(user.Id)?.Level);
        Assert.Equal(1, firstInvite.Uses);
        Assert.Equal(1, secondInvite.Uses);
    }

    [Fact]
    public void A_trust_code_and_an_invite_are_both_redeemed()
    {
        var repo = CreateRepo();
        var invite = CreateInvite(repo, RepoMembershipLevel.Member);

        var user = SignUp(CreateTrustCode(), new RepoInviteRedemption(repo, invite));

        Assert.True(user.IsTrusted);
        Assert.True(repo.HasMember(user.Id));
    }

    [Fact]
    public void An_inactive_trust_code_refuses_the_whole_sign_up()
    {
        var repo = CreateRepo();
        var invite = CreateInvite(repo, RepoMembershipLevel.Member);
        var trustCode = CreateTrustCode();
        trustCode.Revoke(_now);

        Assert.Throws<DomainValidationException>(() => SignUp(trustCode, new RepoInviteRedemption(repo, invite)));
        Assert.Equal(0, invite.Uses);
        Assert.False(repo.HasMember(_newUser));
    }

    [Fact]
    public void An_inactive_invite_refuses_the_sign_up()
    {
        var repo = CreateRepo();
        var invite = CreateInvite(repo, RepoMembershipLevel.Member, maximumUses: 1);
        invite.Redeem(_now, RepoMembershipLevel.Admin);

        Assert.Throws<DomainValidationException>(() => SignUp(null, new RepoInviteRedemption(repo, invite)));
    }

    [Fact]
    public void An_invite_whose_inviter_lost_access_refuses_the_sign_up()
    {
        var repo = CreateRepo();
        var inviter = new User(new UserId("inviter"), new DisplayName("Inviter"), _now);
        repo.AddMember(inviter, RepoMembershipLevel.Guest);
        var invite = new RepoInvite(repo.Id, InviteCodes.Generate(), new RepoInviteRequestId(Guid.NewGuid()), RepoMembershipLevel.Guest, inviter.Id, _now, null, null);

        Assert.Equal(InviteStatus.InviterLostAccess, repo.GetInviteStatus(invite, _now));
        Assert.Throws<DomainValidationException>(() => SignUp(null, new RepoInviteRedemption(repo, invite)));
    }

    [Fact]
    public void Two_invites_to_the_same_repo_are_refused()
    {
        var repo = CreateRepo();

        Assert.Throws<DomainValidationException>(() => SignUp(
            null,
            new RepoInviteRedemption(repo, CreateInvite(repo, RepoMembershipLevel.Member)),
            new RepoInviteRedemption(repo, CreateInvite(repo, RepoMembershipLevel.Guest))));
    }

    [Fact]
    public void An_invite_cannot_be_redeemed_against_another_repo()
    {
        var repo = CreateRepo();
        var invite = CreateInvite(CreateRepo(), RepoMembershipLevel.Member);

        Assert.Throws<DomainValidationException>(() => SignUp(null, new RepoInviteRedemption(repo, invite)));
    }


    private static User SignUp(TrustCode? trustCode, params RepoInviteRedemption[] invites)
        => UserSignUp.Create(_newUser, new DisplayName("New"), _now, trustCode, invites);

    private static TrustCode CreateTrustCode()
        => new(InviteCodes.Generate(), new TrustCodeRequestId(Guid.NewGuid()), _now.AddHours(-1));

    private RepoInvite CreateInvite(Repo repo, RepoMembershipLevel level, int? maximumUses = null)
        => new(repo.Id, InviteCodes.Generate(), new RepoInviteRequestId(Guid.NewGuid()), level, _creator.Id, _now.AddHours(-1), null, maximumUses);

    private Repo CreateRepo() => new(new RepoName("repo"), _now, _creator)
    {
        AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
    };
}
