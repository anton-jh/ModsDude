using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Repos;

public class RepoMembershipTests
{
    private static readonly DateTime _now = new(2026, 1, 1);

    private readonly User _creator = new(new UserId("creator"), new DisplayName("Creator"), _now);
    private readonly User _other = new(new UserId("other"), new DisplayName("Other"), _now);


    [Fact]
    public void The_creator_becomes_an_admin()
    {
        var repo = CreateRepo();

        Assert.Equal(RepoMembershipLevel.Admin, repo.GetMembership(_creator.Id)?.Level);
    }

    [Fact]
    public void A_blocked_user_cannot_create_a_repo()
    {
        _creator.Block(_now);

        Assert.Throws<DomainValidationException>(CreateRepo);
    }

    [Fact]
    public void Adding_a_member_gives_them_the_level_they_were_added_at()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Guest);

        Assert.Equal(RepoMembershipLevel.Guest, repo.GetMembership(_other.Id)?.Level);
    }

    [Fact]
    public void Adding_a_user_who_is_already_a_member_is_refused_and_leaves_their_level_alone()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Guest);

        Assert.Throws<DomainValidationException>(() => repo.AddMember(_other, RepoMembershipLevel.Admin));
        Assert.Equal(RepoMembershipLevel.Guest, repo.GetMembership(_other.Id)?.Level);
    }

    [Fact]
    public void Adding_a_blocked_user_is_refused()
    {
        var repo = CreateRepo();

        _other.Block(_now);

        Assert.Throws<DomainValidationException>(() => repo.AddMember(_other, RepoMembershipLevel.Member));
        Assert.False(repo.HasMember(_other.Id));
    }

    [Fact]
    public void Updating_the_level_of_a_user_who_is_not_a_member_is_refused()
    {
        var repo = CreateRepo();

        Assert.Throws<DomainValidationException>(() => repo.UpdateMembershipLevel(_other.Id, RepoMembershipLevel.Member));
        Assert.False(repo.HasMember(_other.Id));
    }

    [Fact]
    public void Updating_the_level_of_an_existing_member_replaces_it()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Guest);
        repo.UpdateMembershipLevel(_other.Id, RepoMembershipLevel.Admin);

        Assert.Equal(RepoMembershipLevel.Admin, repo.GetMembership(_other.Id)?.Level);
    }

    [Fact]
    public void Kicking_a_member_removes_them()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Member);
        repo.KickMember(_other.Id);

        Assert.False(repo.HasMember(_other.Id));
    }

    [Fact]
    public void Kicking_a_user_who_is_not_a_member_is_refused()
    {
        var repo = CreateRepo();

        Assert.Throws<DomainValidationException>(() => repo.KickMember(_other.Id));
    }

    [Fact]
    public void Kicking_the_only_admin_is_refused()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Member);

        Assert.Throws<DomainValidationException>(() => repo.KickMember(_creator.Id));
        Assert.True(repo.HasMember(_creator.Id));
    }

    [Fact]
    public void Kicking_an_admin_is_allowed_once_another_admin_exists()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Admin);
        repo.KickMember(_creator.Id);

        Assert.False(repo.HasMember(_creator.Id));
    }

    [Fact]
    public void Demoting_the_only_admin_is_refused()
    {
        var repo = CreateRepo();

        Assert.Throws<DomainValidationException>(() => repo.UpdateMembershipLevel(_creator.Id, RepoMembershipLevel.Guest));
        Assert.Equal(RepoMembershipLevel.Admin, repo.GetMembership(_creator.Id)?.Level);
    }

    [Fact]
    public void Demoting_an_admin_is_allowed_once_another_admin_exists()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Admin);
        repo.UpdateMembershipLevel(_creator.Id, RepoMembershipLevel.Member);

        Assert.Equal(RepoMembershipLevel.Member, repo.GetMembership(_creator.Id)?.Level);
    }

    [Fact]
    public void Promoting_the_only_admin_to_admin_again_is_not_a_demotion()
    {
        var repo = CreateRepo();

        repo.UpdateMembershipLevel(_creator.Id, RepoMembershipLevel.Admin);

        Assert.Equal(RepoMembershipLevel.Admin, repo.GetMembership(_creator.Id)?.Level);
    }

    [Fact]
    public void A_user_who_was_never_added_has_no_membership()
    {
        var repo = CreateRepo();

        Assert.False(repo.HasMember(_other.Id));
        Assert.Null(repo.GetMembership(_other.Id));
    }

    [Fact]
    public void Every_membership_change_moves_the_revision_on()
    {
        var repo = CreateRepo();
        var revisions = new List<int> { repo.MembersVersion };

        repo.AddMember(_other, RepoMembershipLevel.Guest);
        revisions.Add(repo.MembersVersion);

        repo.UpdateMembershipLevel(_other.Id, RepoMembershipLevel.Member);
        revisions.Add(repo.MembersVersion);

        repo.KickMember(_other.Id);
        revisions.Add(repo.MembersVersion);

        Assert.Equal(revisions.Distinct().Count(), revisions.Count);
    }

    [Fact]
    public void Setting_the_level_a_member_already_has_leaves_the_revision_alone()
    {
        var repo = CreateRepo();
        repo.AddMember(_other, RepoMembershipLevel.Member);
        var before = repo.MembersVersion;

        repo.UpdateMembershipLevel(_other.Id, RepoMembershipLevel.Member);

        Assert.Equal(before, repo.MembersVersion);
    }

    [Fact]
    public void A_refused_change_leaves_the_revision_alone()
    {
        var repo = CreateRepo();
        var before = repo.MembersVersion;

        Assert.Throws<DomainValidationException>(() => repo.KickMember(_creator.Id));
        Assert.Throws<DomainValidationException>(() => repo.UpdateMembershipLevel(_creator.Id, RepoMembershipLevel.Member));

        Assert.Equal(before, repo.MembersVersion);
    }


    private Repo CreateRepo() => new(new RepoName("repo"), _now, _creator)
    {
        AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
    };
}
