using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests;

/// <summary>
/// The version a client sends back to say what its change was made against moves with every change
/// that client could see, and only with those.
/// </summary>
public class EntityVersionTests
{
    private static readonly DateTime _now = new(2026, 10, 7);
    private readonly User _creator = new(new UserId("creator"), new DisplayName("Creator"), _now);


    [Fact]
    public void A_repo_moves_on_with_every_change_to_it_and_not_with_one_that_changes_nothing()
    {
        var repo = CreateRepo();
        var versions = new List<int> { repo.Version };

        repo.Rename(new RepoName("renamed"));
        versions.Add(repo.Version);
        repo.Rename(new RepoName("renamed"));
        versions.Add(repo.Version);
        repo.Configure(new AdapterConfiguration("""{"x":1}"""));
        versions.Add(repo.Version);
        repo.Configure(new AdapterConfiguration("""{"x":1}"""));
        versions.Add(repo.Version);
        repo.Archive(_now);
        versions.Add(repo.Version);
        repo.Archive(_now);
        versions.Add(repo.Version);
        repo.Restore();
        versions.Add(repo.Version);

        Assert.Equal([0, 1, 1, 2, 2, 3, 3, 4], versions);
    }

    /// <summary>
    /// Two counters, so neither kind of change makes the other kind's editor look stale.
    /// </summary>
    [Fact]
    public void A_repo_counts_its_members_apart_from_itself()
    {
        var repo = CreateRepo();
        var joining = new User(new UserId("joining"), new DisplayName("Joining"), _now);
        var membersBefore = repo.MembersVersion;

        repo.AddMember(joining, RepoMembershipLevel.Member);
        repo.Rename(new RepoName("renamed"));

        Assert.Equal(1, repo.Version);
        Assert.Equal(membersBefore + 1, repo.MembersVersion);
    }

    [Fact]
    public void A_profile_counts_renames_and_archiving_apart_from_its_ignored_mods()
    {
        var profile = new Profile(new RepoId(Guid.NewGuid()), new ProfileName("Season 4"), _now);

        profile.Rename(new ProfileName("Season 5"));
        profile.Rename(new ProfileName("Season 5"));
        profile.Archive(_now);
        profile.Restore();

        Assert.Equal(3, profile.Version);
        Assert.Equal(0, profile.IgnoredModsVersion);

        profile.NoteIgnoredModsReplaced();

        Assert.Equal(3, profile.Version);
        Assert.Equal(1, profile.IgnoredModsVersion);
    }

    [Fact]
    public void A_savegame_moves_on_with_a_rename_and_with_archiving()
    {
        var savegame = new Savegame(new RepoId(Guid.NewGuid()), new SavegameName("Harvest"), null, _now);

        savegame.Rename(new SavegameName("Winter"));
        savegame.Rename(new SavegameName("Winter"));
        savegame.Archive(_now);
        savegame.Archive(_now);
        savegame.Restore(new SavegameName("Spring"));

        Assert.Equal(3, savegame.Version);
        Assert.Equal("Spring", savegame.Name.Value);
    }

    [Fact]
    public void An_invite_moves_on_with_each_use_and_when_it_is_put_away()
    {
        var invite = new RepoInvite(
            new RepoId(Guid.NewGuid()), InviteCodes.Generate(), new RepoInviteRequestId(Guid.NewGuid()),
            RepoMembershipLevel.Member, createdBy: null, _now, null, null);

        invite.Redeem(_now);
        invite.Redeem(_now);
        invite.Revoke(_now);

        Assert.Equal(3, invite.Version);
    }

    [Fact]
    public void A_trust_code_moves_on_when_revoked_and_not_when_revoked_again()
    {
        var code = new TrustCode(InviteCodes.Generate(), new TrustCodeRequestId(Guid.NewGuid()), _now);

        code.Revoke(_now);
        code.Revoke(_now);

        Assert.Equal(1, code.Version);
    }


    private Repo CreateRepo() => new(new RepoName("repo"), _now, _creator)
    {
        AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
    };
}
