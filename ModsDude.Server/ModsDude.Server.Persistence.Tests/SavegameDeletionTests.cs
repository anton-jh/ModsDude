using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// What the database does to a savegame's rows when something above them is deleted, and what it
/// refuses to let anybody delete at all.
/// </summary>
/// <remarks>
/// Three of the four foreign keys around a savegame are declared rather than inferred - two cascades
/// and one <c>Restrict</c> - and each is a decision that would be an ordinary EF default if nobody
/// had written it down. A cascade that quietly became a restrict turns deleting a repo into an
/// unexplainable error; a restrict that quietly became a cascade destroys the mod list a save claims
/// to be reproducible against. Neither shows up anywhere but a real database.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class SavegameDeletionTests(DatabaseFixture fixture)
{
    private static readonly UserId _author = new("author");


    /// <summary>
    /// The bargain Phase 8 accepts knowingly: a version names the one mod list it was played
    /// against, so a profile that has been played can no longer be deleted. The delete endpoint is
    /// expected to report this itself; the constraint is what stops a check-in landing between that
    /// check and the commit from taking the revision with it.
    /// </summary>
    [Fact]
    public async Task Deleting_a_revision_a_savegame_version_was_played_on_is_refused_by_the_database()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenAVersion(repoId, profileId, savegameId, HashOf('1'));

        using var dbContext = fixture.CreateDbContext();

        var revision = await dbContext.ProfileRevisions
            .SingleAsync(x => x.RepoId == repoId && x.ProfileId == profileId && x.Number == new RevisionNumber(1), CancellationToken.None);

        dbContext.ProfileRevisions.Remove(revision);

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// Nothing outside a savegame addresses one of its versions, so a history whose savegame is gone
    /// is not a record of anything. The cascade is at the database rather than in the endpoint
    /// because there is no navigation from a savegame to its versions to walk - see
    /// <see cref="Savegame"/> for why that navigation deliberately does not exist.
    /// </summary>
    [Fact]
    public async Task Deleting_a_savegame_takes_its_versions_with_it()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);
        var survivor = await GivenASavegame(repoId, profileId);

        await GivenAVersion(repoId, profileId, savegameId, HashOf('1'));
        await GivenAVersion(repoId, profileId, savegameId, HashOf('2'));
        await GivenAVersion(repoId, profileId, survivor, HashOf('3'));

        await GivenTheSavegameIsDeleted(repoId, savegameId);

        using var verification = fixture.CreateDbContext();

        Assert.Equal(0, await verification.SavegameVersions.CountVersionsAsync(repoId, savegameId, CancellationToken.None));
        Assert.Equal(1, await verification.SavegameVersions.CountVersionsAsync(repoId, survivor, CancellationToken.None));
    }

    /// <summary>
    /// The claim log outlives the blobs and survives pruning, but it does not outlive the thing it is
    /// a log of. An open claim especially: leaving one behind would hold a filtered-unique slot
    /// against a savegame id that could be reused.
    /// </summary>
    [Fact]
    public async Task Deleting_a_savegame_takes_its_claims_with_it()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenAnOpenCheckout(repoId, savegameId);
        await GivenTheSavegameIsDeleted(repoId, savegameId);

        using var verification = fixture.CreateDbContext();

        Assert.Equal(0, await verification.SavegameCheckouts.CountCheckoutsAsync(repoId, savegameId, CancellationToken.None));
    }

    /// <summary>
    /// A savegame outside a repo is addressable by nothing, and the blobs behind it are reclaimed by
    /// the sweep afterwards. The interesting part is that the cascade has to reach through the
    /// savegame to its versions while those versions still hold a <c>Restrict</c> key onto the
    /// profile revisions the same delete is removing.
    /// </summary>
    [Fact]
    public async Task Deleting_a_repo_takes_its_savegames_and_everything_under_them()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId);

        await GivenAVersion(repoId, profileId, savegameId, HashOf('1'));
        await GivenAnOpenCheckout(repoId, savegameId);

        using (var dbContext = fixture.CreateDbContext())
        {
            var repo = await dbContext.Repos.SingleAsync(x => x.Id == repoId, CancellationToken.None);

            dbContext.Repos.Remove(repo);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();

        Assert.Empty(await verification.Savegames.GetRowsAsync(repoId, CancellationToken.None));
        Assert.Equal(0, await verification.SavegameVersions.CountVersionsAsync(repoId, savegameId, CancellationToken.None));
        Assert.Equal(0, await verification.SavegameCheckouts.CountCheckoutsAsync(repoId, savegameId, CancellationToken.None));
    }

    /// <summary>
    /// A savegame's name is what people say to each other, so it has to mean one thing inside a repo
    /// - and it is checked by a unique index rather than by the endpoint, so two people publishing
    /// "Season 4" at the same moment produce one savegame and one refusal.
    /// </summary>
    [Fact]
    public async Task Two_savegames_in_one_repo_may_not_share_a_name()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();

        await GivenASavegame(repoId, profileId, "Season 4");

        using var dbContext = fixture.CreateDbContext();

        dbContext.Savegames.Add(new Savegame(repoId, new SavegameName("Season 4"), profileId, DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// The index is scoped to the repo, which is the whole point of it: every group names its first
    /// save something obvious, and one group getting there first must not take the word away from
    /// everybody else.
    /// </summary>
    [Fact]
    public async Task Two_repos_may_each_hold_a_savegame_of_the_same_name()
    {
        var (firstRepo, firstProfile) = await GivenARepoWithAProfile();
        var (secondRepo, secondProfile) = await GivenARepoWithAProfile();

        await GivenASavegame(firstRepo, firstProfile, "Main farm");
        await GivenASavegame(secondRepo, secondProfile, "Main farm");

        using var dbContext = fixture.CreateDbContext();

        Assert.True(await dbContext.Savegames.CheckNameIsTaken(firstRepo, new SavegameName("Main farm"), CancellationToken.None));
        Assert.True(await dbContext.Savegames.CheckNameIsTaken(secondRepo, new SavegameName("Main farm"), CancellationToken.None));
    }

    /// <summary>
    /// Renaming a savegame to the name it already has is not a collision, which the overload taking
    /// an exclusion exists for. Without it, saving a rename form unchanged would refuse itself.
    /// </summary>
    [Fact]
    public async Task A_savegame_does_not_collide_with_its_own_name()
    {
        var (repoId, profileId) = await GivenARepoWithAProfile();
        var savegameId = await GivenASavegame(repoId, profileId, "Season 5");
        var other = await GivenASavegame(repoId, profileId, "Season 6");

        using var dbContext = fixture.CreateDbContext();

        Assert.False(await dbContext.Savegames.CheckNameIsTaken(repoId, savegameId, new SavegameName("Season 5"), CancellationToken.None));
        Assert.True(await dbContext.Savegames.CheckNameIsTaken(repoId, savegameId, new SavegameName("Season 6"), CancellationToken.None));
        Assert.True(await dbContext.Savegames.CheckNameIsTaken(repoId, other, new SavegameName("Season 5"), CancellationToken.None));
    }


    private static string HashOf(char character) => new(character, ModImageHash.Length);

    private async Task<(RepoId RepoId, ProfileId ProfileId)> GivenARepoWithAProfile()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        var profile = new Profile(repo.Id, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);
        var revision = profile.CreateRevision([], [], _author, DateTime.UtcNow, origin: ProfileRevisionOrigin.Created);

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, profile.Id);
    }

    private async Task<SavegameId> GivenASavegame(RepoId repoId, ProfileId profileId, string? name = null)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName(name ?? $"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return savegame.Id;
    }

    private async Task GivenAVersion(RepoId repoId, ProfileId profileId, SavegameId savegameId, string contentHash)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;

        var version = savegame.CreateVersion(
            profileId,
            new RevisionNumber(1),
            contentHash,
            sizeBytes: 1024,
            _author,
            DateTime.UtcNow);

        dbContext.SavegameVersions.Add(version);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task GivenAnOpenCheckout(RepoId repoId, SavegameId savegameId)
    {
        using var dbContext = fixture.CreateDbContext();

        dbContext.SavegameCheckouts.Add(new SavegameCheckout(repoId, savegameId, _author, DateTime.UtcNow));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task GivenTheSavegameIsDeleted(RepoId repoId, SavegameId savegameId)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None);

        dbContext.Savegames.Remove(savegame!);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
