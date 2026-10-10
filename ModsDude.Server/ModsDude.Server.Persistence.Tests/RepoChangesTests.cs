using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Changes;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The per-repo change counters a client polls, which the database keeps itself: see <see cref="RepoChangeCounter"/>.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class RepoChangesTests(DatabaseFixture fixture)
{
    private static readonly GameKey _game = new("_farming_simulator#fs25");


    [Fact]
    public async Task Only_the_callers_live_repos_are_listed_in_id_order()
    {
        var caller = await GivenAUser();
        var stranger = await GivenAUser();
        var first = await GivenARepo(caller);
        var second = await GivenARepo(caller);
        var archived = await GivenARepo(caller);
        await GivenARepo(stranger);

        await Change(archived.RepoId, repo => repo.Archive(DateTime.UtcNow));

        var listed = await Read(caller);

        Assert.Equal(new[] { first.RepoId, second.RepoId }.OrderBy(x => x.Value), listed.Select(x => x.RepoId));
    }

    [Fact]
    public async Task Renaming_a_repo_moves_its_repo_counter_only()
    {
        var caller = await GivenAUser();
        var (repoId, _) = await GivenARepo(caller);
        var before = await ReadOne(caller, repoId);

        await Change(repoId, repo => repo.Rename(new RepoName($"renamed-{Guid.NewGuid()}")));

        Assert.Equal(before with { Repo = before.Repo + 1 }, await ReadOne(caller, repoId));
    }

    [Fact]
    public async Task Creating_renaming_and_deleting_a_profile_each_move_the_profiles_counter_only()
    {
        var caller = await GivenAUser();
        var (repoId, profileId) = await GivenARepo(caller);
        var before = await ReadOne(caller, repoId);

        using (var dbContext = fixture.CreateDbContext())
        {
            var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;
            profile.Rename(new ProfileName($"renamed-{Guid.NewGuid()}"));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var other = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow, new ProfileCreateRequestId(Guid.NewGuid()));

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Profiles.Add(other);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Profiles.Where(x => x.RepoId == repoId && x.Id == other.Id).ExecuteDeleteAsync(CancellationToken.None);
        }

        Assert.Equal(before with { Profiles = before.Profiles + 3 }, await ReadOne(caller, repoId));
    }

    [Fact]
    public async Task A_savegame_its_snapshot_and_its_claim_each_move_the_savegames_counter_only()
    {
        var caller = await GivenAUser();
        var (repoId, profileId) = await GivenARepo(caller);
        var before = await ReadOne(caller, repoId);

        using (var dbContext = fixture.CreateDbContext())
        {
            var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

            dbContext.Savegames.Add(savegame);
            dbContext.SavegameSnapshots.Add(savegame.CreateSnapshot(
                new RevisionNumber(1), new string('1', ModImageHash.Length), sizeBytes: 1024, caller, DateTime.UtcNow));
            dbContext.SavegameCheckouts.Add(new SavegameCheckout(repoId, savegame.Id, caller, DateTime.UtcNow));

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal(before with { Savegames = before.Savegames + 3 }, await ReadOne(caller, repoId));
    }

    [Fact]
    public async Task Somebody_joining_moves_the_members_counter()
    {
        var caller = await GivenAUser();
        var joining = await GivenAUser();
        var (repoId, _) = await GivenARepo(caller);
        var before = await ReadOne(caller, repoId);

        using (var dbContext = fixture.CreateDbContext())
        {
            var repo = (await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!;
            var user = (await dbContext.Users.GetAsync(joining, CancellationToken.None))!;

            repo.AddMember(user, RepoMembershipLevel.Member);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var after = await ReadOne(caller, repoId);

        Assert.Equal(before.Members + 1, after.Members);
        Assert.Equal((before.Profiles, before.Savegames, before.Mods, before.Activity), (after.Profiles, after.Savegames, after.Mods, after.Activity));
    }

    [Fact]
    public async Task A_member_switching_profile_moves_the_activity_counter_only()
    {
        var caller = await GivenAUser();
        var (repoId, profileId) = await GivenARepo(caller);
        var before = await ReadOne(caller, repoId);

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.GameActivities.Add(new GameActivity(
                caller, _game, repoId, profileId, null, GameActivityKind.Activated, null, DateTime.UtcNow));

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Equal(before with { Activity = before.Activity + 1 }, await ReadOne(caller, repoId));
    }

    [Fact]
    public async Task A_change_in_one_repo_leaves_another_repos_counters_alone()
    {
        var caller = await GivenAUser();
        var (changed, _) = await GivenARepo(caller);
        var (untouched, _) = await GivenARepo(caller);
        var before = await ReadOne(caller, untouched);

        await Change(changed, repo => repo.Rename(new RepoName($"renamed-{Guid.NewGuid()}")));

        Assert.Equal(before, await ReadOne(caller, untouched));
    }

    /// <summary>
    /// Two writers in one repo at once: the second waits on the counter row until the first commits,
    /// so neither increment is lost.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_writes_in_one_repo_are_both_counted()
    {
        var caller = await GivenAUser();
        var (repoId, profileId) = await GivenARepo(caller);
        var other = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow, new ProfileCreateRequestId(Guid.NewGuid()));

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.Profiles.Add(other);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        var before = await ReadOne(caller, repoId);

        using var first = fixture.CreateDbContext();
        using var second = fixture.CreateDbContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync(CancellationToken.None);
        await using var secondTransaction = await second.Database.BeginTransactionAsync(CancellationToken.None);

        (await first.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!.Rename(new ProfileName($"first-{Guid.NewGuid()}"));
        await first.SaveChangesAsync(CancellationToken.None);

        (await second.Profiles.GetAsync(repoId, other.Id, CancellationToken.None))!.Rename(new ProfileName($"second-{Guid.NewGuid()}"));
        var secondSave = second.SaveChangesAsync(CancellationToken.None);

        await firstTransaction.CommitAsync(CancellationToken.None);
        await secondSave;
        await secondTransaction.CommitAsync(CancellationToken.None);

        Assert.Equal(before.Profiles + 2, (await ReadOne(caller, repoId)).Profiles);
    }

    /// <summary>The rows a repo delete cascades away would count against a repo that is gone; they count nothing instead.</summary>
    [Fact]
    public async Task A_repo_with_contents_can_still_be_deleted()
    {
        var caller = await GivenAUser();
        var (repoId, profileId) = await GivenARepo(caller);

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.GameActivities.Add(new GameActivity(
                caller, _game, repoId, profileId, null, GameActivityKind.Activated, null, DateTime.UtcNow));

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            var repo = (await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!;

            await dbContext.DeleteWithContentsAsync(repo, CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        Assert.False(await verification.RepoChangeCounters.AnyAsync(x => x.RepoId == repoId, CancellationToken.None));
        Assert.Empty(await Read(caller));
    }


    private async Task<List<RepoChanges>> Read(UserId caller)
    {
        using var dbContext = fixture.CreateDbContext();

        return await RepoChanges.ReadForAsync(dbContext, caller, CancellationToken.None);
    }

    private async Task<RepoChanges> ReadOne(UserId caller, RepoId repoId)
        => (await Read(caller)).Single(x => x.RepoId == repoId);

    private async Task Change(RepoId repoId, Action<Repo> change)
    {
        using var dbContext = fixture.CreateDbContext();

        var repo = (await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!;
        change(repo);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<UserId> GivenAUser()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return userId;
    }

    private async Task<(RepoId RepoId, ProfileId ProfileId)> GivenARepo(UserId admin)
    {
        using var dbContext = fixture.CreateDbContext();

        var adminUser = (await dbContext.Users.GetAsync(admin, CancellationToken.None))!;

        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, adminUser)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        var profile = new Profile(repo.Id, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow, new ProfileCreateRequestId(Guid.NewGuid()));
        var revision = profile.CreateRevision([], [], admin, DateTime.UtcNow, origin: ProfileRevisionOrigin.Created);

        dbContext.Repos.Add(repo);
        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, profile.Id);
    }
}
