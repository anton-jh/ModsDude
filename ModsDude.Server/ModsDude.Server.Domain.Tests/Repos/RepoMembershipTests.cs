using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Repos;

public class RepoMembershipTests
{
    private static readonly UserId _creator = new("creator");
    private static readonly UserId _other = new("other");


    [Fact]
    public void The_creator_becomes_an_admin()
    {
        var repo = CreateRepo();

        Assert.Equal(RepoMembershipLevel.Admin, repo.GetMembership(_creator)?.Level);
    }

    [Fact]
    public void Adding_a_member_gives_them_the_level_they_were_added_at()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Guest);

        Assert.Equal(RepoMembershipLevel.Guest, repo.GetMembership(_other)?.Level);
    }

    [Fact]
    public void Adding_a_user_who_is_already_a_member_throws()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Guest);

        Assert.Throws<InvalidOperationException>(() => repo.AddMember(_other, RepoMembershipLevel.Member));
    }

    [Fact]
    public void Adding_a_user_who_is_already_a_member_leaves_their_level_alone()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Guest);

        Assert.Throws<InvalidOperationException>(() => repo.AddMember(_other, RepoMembershipLevel.Admin));
        Assert.Equal(RepoMembershipLevel.Guest, repo.GetMembership(_other)?.Level);
    }

    [Fact]
    public void Updating_the_level_of_a_user_who_is_not_a_member_adds_them()
    {
        var repo = CreateRepo();

        repo.UpdateMembershipLevel(_other, RepoMembershipLevel.Member);

        Assert.True(repo.HasMember(_other));
        Assert.Equal(RepoMembershipLevel.Member, repo.GetMembership(_other)?.Level);
    }

    [Fact]
    public void Updating_the_level_of_an_existing_member_replaces_it()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Guest);
        repo.UpdateMembershipLevel(_other, RepoMembershipLevel.Admin);

        Assert.Equal(RepoMembershipLevel.Admin, repo.GetMembership(_other)?.Level);
    }

    [Fact]
    public void Kicking_a_member_removes_them()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Member);
        repo.KickMember(_other);

        Assert.False(repo.HasMember(_other));
    }

    [Fact]
    public void Kicking_a_user_who_is_not_a_member_throws()
    {
        var repo = CreateRepo();

        Assert.Throws<InvalidOperationException>(() => repo.KickMember(_other));
    }

    [Fact]
    public void Kicking_the_only_admin_throws()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Member);

        Assert.Throws<InvalidOperationException>(() => repo.KickMember(_creator));
        Assert.True(repo.HasMember(_creator));
    }

    [Fact]
    public void Kicking_an_admin_is_allowed_once_another_admin_exists()
    {
        var repo = CreateRepo();

        repo.AddMember(_other, RepoMembershipLevel.Admin);
        repo.KickMember(_creator);

        Assert.False(repo.HasMember(_creator));
    }

    [Fact]
    public void Demoting_the_only_admin_is_not_stopped_by_the_kick_rule()
    {
        // The only-Admin rule guards KickMember alone. A repo can be left with no Admin at all by
        // demoting instead, which no endpoint prevents either.
        var repo = CreateRepo();

        repo.UpdateMembershipLevel(_creator, RepoMembershipLevel.Guest);

        Assert.False(repo.IsOnlyAdmin(_creator));
    }

    [Fact]
    public void A_user_who_was_never_added_has_no_membership()
    {
        var repo = CreateRepo();

        Assert.False(repo.HasMember(_other));
        Assert.Null(repo.GetMembership(_other));
    }


    private static Repo CreateRepo() => new(new RepoName("repo"), new DateTime(2026, 1, 1), _creator)
    {
        AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
    };
}
