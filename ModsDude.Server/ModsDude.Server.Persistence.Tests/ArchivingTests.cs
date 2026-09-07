using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// What archiving does to a name, which is the half of it only a database can answer: a profile's
/// and a savegame's uniqueness index is filtered on <c>ArchivedAt</c>, so "an archived entity does
/// not hold its name" is a partial index rather than anything the model can enforce. Repos have no
/// such index and no such rule, and the tests here say so.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ArchivingTests(DatabaseFixture fixture)
{
    private static readonly DateTime _archivedAt = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public async Task An_archived_profile_frees_its_name_for_a_new_one()
    {
        var repoId = await GivenARepo();
        var profileId = await GivenAProfile(repoId, "Season 4");

        await ArchiveProfile(repoId, profileId);

        using var dbContext = fixture.CreateDbContext();

        Assert.False(await dbContext.Profiles.CheckNameIsTaken(repoId, new ProfileName("Season 4"), CancellationToken.None));

        dbContext.Profiles.Add(new Profile(repoId, new ProfileName("Season 4"), DateTime.UtcNow));

        // The index has to permit it too, not merely the check above.
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Several archived things may share one name - they are told apart by when they were archived,
    /// which is why the timestamp is on the DTO rather than being an implementation detail.
    /// </summary>
    [Fact]
    public async Task Any_number_of_archived_profiles_may_share_a_name()
    {
        var repoId = await GivenARepo();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var profileId = await GivenAProfile(repoId, "Season 4");

            await ArchiveProfile(repoId, profileId);
        }

        using var dbContext = fixture.CreateDbContext();

        var archived = await dbContext.Profiles
            .Where(x => x.RepoId == repoId && x.ArchivedAt != null && x.Name == new ProfileName("Season 4"))
            .CountAsync(CancellationToken.None);

        Assert.Equal(3, archived);
    }

    /// <summary>The filtered index still does its job for the ones that are live.</summary>
    [Fact]
    public async Task Two_live_profiles_still_cannot_share_a_name()
    {
        var repoId = await GivenARepo();

        await GivenAProfile(repoId, "Season 4");

        using var dbContext = fixture.CreateDbContext();

        dbContext.Profiles.Add(new Profile(repoId, new ProfileName("Season 4"), DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// The clash the archive defers rather than prevents: restoring is where it lands, and refusing
    /// there is what makes renaming the answer.
    /// </summary>
    [Fact]
    public async Task Restoring_into_a_name_that_has_been_taken_is_refused_by_the_index()
    {
        var repoId = await GivenARepo();
        var profileId = await GivenAProfile(repoId, "Season 4");

        await ArchiveProfile(repoId, profileId);
        await GivenAProfile(repoId, "Season 4");

        using var dbContext = fixture.CreateDbContext();

        var archived = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;

        Assert.True(await dbContext.Profiles.CheckNameIsTaken(repoId, archived.Id, archived.Name, CancellationToken.None));

        archived.Restore();

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Restoring_under_another_name_is_how_the_clash_is_resolved()
    {
        var repoId = await GivenARepo();
        var profileId = await GivenAProfile(repoId, "Season 4");

        await ArchiveProfile(repoId, profileId);
        await GivenAProfile(repoId, "Season 4");

        using var dbContext = fixture.CreateDbContext();

        var archived = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;

        archived.Restore(new ProfileName("Season 4 (old)"));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Null(archived.ArchivedAt);
    }

    /// <summary>
    /// Archiving twice must not move the timestamp: it is the only thing telling two archived things
    /// of one name apart, so restamping would silently reorder somebody's archive.
    /// </summary>
    [Fact]
    public async Task Archiving_something_twice_does_not_restamp_it()
    {
        var repoId = await GivenARepo();
        var profileId = await GivenAProfile(repoId, "Season 4");

        await ArchiveProfile(repoId, profileId);

        using var dbContext = fixture.CreateDbContext();

        var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;

        profile.Archive(_archivedAt.AddYears(1));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(_archivedAt, profile.ArchivedAt);
    }

    [Fact]
    public async Task An_archived_savegame_frees_its_name_too()
    {
        var repoId = await GivenARepo();
        var profileId = await GivenAProfile(repoId, "Season 4");
        var savegameId = await GivenASavegame(repoId, profileId, "The farm");

        using (var dbContext = fixture.CreateDbContext())
        {
            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

            savegame.Archive(_archivedAt);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            Assert.False(await dbContext.Savegames.CheckNameIsTaken(repoId, new SavegameName("The farm"), CancellationToken.None));

            dbContext.Savegames.Add(new Savegame(repoId, new SavegameName("The farm"), profileId, DateTime.UtcNow));

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Unlike profiles and savegames, whose names are unique within their repo, repo names are not
    /// unique at all - so archiving one frees nothing, and there is nothing here for the archive to
    /// defer to a restore.
    /// </summary>
    [Fact]
    public async Task Two_live_repos_may_share_a_name()
    {
        var name = $"repo-{Guid.NewGuid()}";

        var first = await GivenARepo(name);

        using var dbContext = fixture.CreateDbContext();

        AddRepo(dbContext, name);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var both = await dbContext.Repos
            .Where(x => x.Name == new RepoName(name))
            .ToListAsync(CancellationToken.None);

        Assert.Equal(2, both.Count);

        // The whole point of allowing it: they are still told apart, by something neither of them
        // had to be renamed for.
        Assert.NotEqual(RepoTag.For(both[0].Id), RepoTag.For(both[1].Id));
        Assert.Contains(both, x => x.Id == first);
    }

    [Fact]
    public async Task An_archived_repo_comes_back_under_its_own_name()
    {
        var name = $"repo-{Guid.NewGuid()}";
        var repoId = await GivenARepo(name);

        using (var dbContext = fixture.CreateDbContext())
        {
            var repo = (await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!;

            repo.Archive(_archivedAt);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        // Somebody else takes the name while it is away, which is exactly the case that used to cost
        // the restore a rename.
        using (var dbContext = fixture.CreateDbContext())
        {
            AddRepo(dbContext, name);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            var repo = (await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!;

            repo.Restore();

            await dbContext.SaveChangesAsync(CancellationToken.None);

            Assert.Equal(new RepoName(name), repo.Name);
            Assert.False(repo.IsArchived);
        }
    }


    private async Task ArchiveProfile(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;

        profile.Archive(_archivedAt);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<RepoId> GivenARepo(string? name = null)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = NewRepo(userId, name);

        dbContext.Users.Add(new User(userId, new DisplayName("user"), DateTime.UtcNow));
        dbContext.Repos.Add(repo);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    /// <summary>
    /// A repo and the user its first membership names. Both, because the membership the constructor
    /// mints has a foreign key onto Users - and a test that forgets it fails with the same exception
    /// type as the index it meant to be testing.
    /// </summary>
    private static Repo AddRepo(DbContexts.ApplicationDbContext dbContext, string? name = null)
    {
        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = NewRepo(userId, name);

        dbContext.Users.Add(new User(userId, new DisplayName("user"), DateTime.UtcNow));
        dbContext.Repos.Add(repo);

        return repo;
    }

    private static Repo NewRepo(UserId? firstAdmin = null, string? name = null)
    {
        return new Repo(
            new RepoName(name ?? $"repo-{Guid.NewGuid()}"),
            DateTime.UtcNow,
            firstAdmin ?? new UserId($"user-{Guid.NewGuid()}"))
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };
    }

    private async Task<ProfileId> GivenAProfile(RepoId repoId, string name)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = new Profile(repoId, new ProfileName(name), DateTime.UtcNow);

        dbContext.Profiles.Add(profile);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return profile.Id;
    }

    private async Task<SavegameId> GivenASavegame(RepoId repoId, ProfileId profileId, string name)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName(name), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return savegame.Id;
    }
}
