using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// Who sees whose game. The rules are all membership joins, which is exactly the kind of thing an
/// in-memory substitute answers for itself - so they are asked of PostgreSQL.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class GameActivityQueryTests(DatabaseFixture fixture)
{
    private static readonly GameKey _game = new("_farming_simulator#fs25");
    private static readonly DateTime _now = new(2026, 9, 25, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _weekAgo = _now.AddDays(-7);


    [Fact]
    public async Task A_friend_in_a_shared_repo_is_seen()
    {
        var (viewer, friend, repoId, profileId) = await GivenTwoMembersOfARepo();

        await GivenActivity(friend, repoId, profileId, _now.AddHours(-1));

        var row = Assert.Single(await Query(viewer));

        Assert.Equal(friend, row.User.Id);
        Assert.Equal(profileId, row.Activity.ProfileId);
    }

    [Fact]
    public async Task The_viewers_own_games_are_left_out()
    {
        var (viewer, _, repoId, profileId) = await GivenTwoMembersOfARepo();

        await GivenActivity(viewer, repoId, profileId, _now.AddHours(-1));

        Assert.Empty(await Query(viewer));
    }

    /// <summary>A repo the viewer is not in is none of their business, whoever else is in it.</summary>
    [Fact]
    public async Task A_repo_the_viewer_is_not_in_is_not_seen()
    {
        var (_, friend, repoId, profileId) = await GivenTwoMembersOfARepo();
        var stranger = await GivenAUser();

        await GivenActivity(friend, repoId, profileId, _now.AddHours(-1));

        Assert.Empty(await Query(stranger));
    }

    /// <summary>Somebody who has left a repo is not playing in it with anybody.</summary>
    [Fact]
    public async Task A_friend_who_has_left_the_repo_is_not_seen()
    {
        var (viewer, friend, repoId, profileId) = await GivenTwoMembersOfARepo();

        await GivenActivity(friend, repoId, profileId, _now.AddHours(-1));

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.RepoMemberships.Remove(dbContext.RepoMemberships.Single(x => x.RepoId == repoId && x.UserId == friend));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Empty(await Query(viewer));
    }

    [Fact]
    public async Task Activity_older_than_the_window_is_not_seen()
    {
        var (viewer, friend, repoId, profileId) = await GivenTwoMembersOfARepo();

        await GivenActivity(friend, repoId, profileId, _weekAgo.AddMinutes(-1));

        Assert.Empty(await Query(viewer));
    }

    [Fact]
    public async Task Only_the_asked_repo_is_seen_when_one_is_asked_for()
    {
        var (viewer, friend, repoId, profileId) = await GivenTwoMembersOfARepo();
        var (otherRepoId, otherProfileId) = await GivenARepo(viewer, friend);

        await GivenActivity(friend, repoId, profileId, _now.AddHours(-1));
        await GivenActivity(friend, otherRepoId, otherProfileId, _now.AddHours(-1), new GameKey("_other"));

        using var dbContext = fixture.CreateDbContext();

        var row = Assert.Single(await dbContext.GetVisibleToAsync(viewer, otherRepoId, _weekAgo, CancellationToken.None));

        Assert.Equal(otherRepoId, row.Activity.RepoId);
    }

    [Fact]
    public async Task The_checked_out_savegame_is_named()
    {
        var (viewer, friend, repoId, profileId) = await GivenTwoMembersOfARepo();

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, _now);

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Savegames.Add(savegame);
            dbContext.GameActivities.Add(new GameActivity(
                friend, _game, repoId, profileId, new RevisionNumber(1), GameActivityKind.SavegameCheckedOut, savegame.Id, _now.AddHours(-1)));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var row = Assert.Single(await Query(viewer));

        Assert.Equal(savegame.Name, row.SavegameName);
        Assert.Equal(GameActivityKind.SavegameCheckedOut, row.Activity.Kind);
    }

    /// <summary>A deleted profile is one nobody can be on, and a row naming it is a friend on nothing.</summary>
    [Fact]
    public async Task Deleting_the_profile_removes_the_row()
    {
        var (_, friend, repoId, profileId) = await GivenTwoMembersOfARepo();

        await GivenActivity(friend, repoId, profileId, _now.AddHours(-1));

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Profiles.Remove((await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using var readContext = fixture.CreateDbContext();

        Assert.Null(await readContext.GameActivities.GetAsync(friend, _game, CancellationToken.None));
    }


    private async Task<List<VisibleGameActivity>> Query(UserId viewer)
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.GetVisibleToAsync(viewer, null, _weekAgo, CancellationToken.None);
    }

    private async Task<(UserId Viewer, UserId Friend, RepoId RepoId, ProfileId ProfileId)> GivenTwoMembersOfARepo()
    {
        var viewer = await GivenAUser();
        var friend = await GivenAUser();
        var (repoId, profileId) = await GivenARepo(viewer, friend);

        return (viewer, friend, repoId, profileId);
    }

    private async Task<UserId> GivenAUser()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return userId;
    }

    private async Task<(RepoId RepoId, ProfileId ProfileId)> GivenARepo(UserId admin, UserId member)
    {
        using var dbContext = fixture.CreateDbContext();

        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, admin)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        repo.AddMember(member, RepoMembershipLevel.Member);

        var profile = new Profile(repo.Id, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);
        var revision = profile.CreateRevision([], [], admin, DateTime.UtcNow, origin: ProfileRevisionOrigin.Created);

        dbContext.Repos.Add(repo);
        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, profile.Id);
    }

    private async Task GivenActivity(UserId userId, RepoId repoId, ProfileId profileId, DateTime at, GameKey? game = null)
    {
        using var dbContext = fixture.CreateDbContext();

        dbContext.GameActivities.Add(new GameActivity(
            userId, game ?? _game, repoId, profileId, null, GameActivityKind.Activated, null, at));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
