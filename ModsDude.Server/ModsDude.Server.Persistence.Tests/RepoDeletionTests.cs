using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// Deleting a repo that is not empty, which is the one delete allowed to take everything with it.
/// </summary>
/// <remarks>
/// Every other delete in the system refuses over a dependant - a revision that pins a mod version, a
/// savegame played on a revision - and those refusals are foreign keys declared <c>Restrict</c>. A
/// repo delete removes the dependants and the dependencies together, so the only thing standing
/// between it and a foreign key violation is the order it does that in. That order is a property of
/// PostgreSQL rather than of the model, so this is the only place it can be asserted.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class RepoDeletionTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ModId _modId = new("FS25_TestMod");
    private static readonly UserId _author = new("author");


    /// <summary>
    /// The whole point: a repo with everything in it at once - two mod versions, one of them pinned
    /// only by a revision the profile has since moved off, a savegame played on that revision, an
    /// open claim on the savegame, and an unspent invite.
    /// </summary>
    [Fact]
    public async Task Deleting_a_repo_takes_everything_in_it()
    {
        var repoId = await GivenAFullRepo();

        await GivenTheRepoIsDeleted(repoId);

        using var verification = fixture.CreateDbContext();

        Assert.Null(await verification.Repos.GetAsync(repoId, CancellationToken.None));
        Assert.Equal(0, await verification.ModVersions.CountAsync(x => x.RepoId == repoId, CancellationToken.None));
        Assert.Equal(0, await verification.Profiles.CountAsync(x => x.RepoId == repoId, CancellationToken.None));
        Assert.Equal(0, await verification.ProfileRevisions.CountAsync(x => x.RepoId == repoId, CancellationToken.None));
        Assert.Equal(0, await verification.Savegames.CountAsync(x => x.RepoId == repoId, CancellationToken.None));
        Assert.Equal(0, await verification.SavegameVersions.CountAsync(x => x.RepoId == repoId, CancellationToken.None));
        Assert.Equal(0, await verification.SavegameCheckouts.CountAsync(x => x.RepoId == repoId, CancellationToken.None));
        Assert.Equal(0, await verification.RepoInvites.CountAsync(x => x.RepoId == repoId, CancellationToken.None));
        Assert.Equal(0, await verification.RepoMemberships.CountAsync(x => x.RepoId == repoId, CancellationToken.None));
    }

    /// <summary>
    /// The repo next door is untouched. <c>ExecuteDelete</c> writes its own <c>WHERE</c> rather than
    /// working from a loaded graph, so scoping each of them to the repo is a thing to get wrong.
    /// </summary>
    [Fact]
    public async Task Deleting_a_repo_leaves_another_repo_alone()
    {
        var repoId = await GivenAFullRepo();
        var survivor = await GivenAFullRepo();

        await GivenTheRepoIsDeleted(repoId);

        using var verification = fixture.CreateDbContext();

        Assert.Equal(2, await verification.ModVersions.CountAsync(x => x.RepoId == survivor, CancellationToken.None));
        Assert.Equal(1, await verification.Profiles.CountAsync(x => x.RepoId == survivor, CancellationToken.None));
        Assert.Equal(2, await verification.ProfileRevisions.CountAsync(x => x.RepoId == survivor, CancellationToken.None));
        Assert.Equal(1, await verification.Savegames.CountAsync(x => x.RepoId == survivor, CancellationToken.None));
        Assert.Equal(1, await verification.SavegameVersions.CountAsync(x => x.RepoId == survivor, CancellationToken.None));
        Assert.Equal(1, await verification.SavegameCheckouts.CountAsync(x => x.RepoId == survivor, CancellationToken.None));
        Assert.Equal(1, await verification.RepoInvites.CountAsync(x => x.RepoId == survivor, CancellationToken.None));
    }

    /// <summary>
    /// Why <see cref="RepoExtensions.EmptyAsync"/> exists at all. The mod versions are the innermost
    /// <c>Restrict</c>, so a repo row removed on its own walks straight into it - which is what the
    /// delete endpoint used to do, and then had to refuse the case rather than handle it.
    /// </summary>
    [Fact]
    public async Task Deleting_the_repo_row_on_its_own_is_refused_by_the_database()
    {
        var repoId = await GivenAFullRepo();

        using var dbContext = fixture.CreateDbContext();

        dbContext.Repos.Remove((await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!);

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }


    private async Task<RepoId> GivenAFullRepo()
    {
        var (repoId, profileId) = await GivenARepoWithAProfilePinningAMod();

        await GivenASavegamePlayedOnTheFirstRevision(repoId, profileId);
        await GivenTheProfileMovesTo(repoId, profileId, "2.0.0");
        await GivenAnInvite(repoId);

        return repoId;
    }

    private async Task<(RepoId RepoId, ProfileId ProfileId)> GivenARepoWithAProfilePinningAMod()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };
        var versions = new[] { CreateVersion(repo.Id, "1.0.0", 0), CreateVersion(repo.Id, "2.0.0", 1) };

        var profile = new Profile(repo.Id, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);
        var revision = profile.CreateRevision(
            [new ModDependency { ModVersion = versions[0], Locked = false }],
            [],
            _author,
            DateTime.UtcNow);

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.ModVersions.AddRange(versions);
        dbContext.Profiles.Add(profile);
        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, profile.Id);
    }

    /// <summary>
    /// A second revision, so the repo also holds a mod version that only history pins - the case that
    /// makes a version undeletable on its own.
    /// </summary>
    private async Task GivenTheProfileMovesTo(RepoId repoId, ProfileId profileId, string versionId)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = (await dbContext.Profiles.GetAsync(repoId, profileId, CancellationToken.None))!;
        var version = await dbContext.ModVersions.GetAsync(repoId, _modId, new ModVersionId(versionId), CancellationToken.None);
        var previous = await dbContext.ProfileRevisions.GetPinsAsync(repoId, profileId, profile.HeadRevision, CancellationToken.None);

        var revision = profile.CreateRevision(
            [new ModDependency { ModVersion = version!, Locked = false }],
            previous,
            _author,
            DateTime.UtcNow);

        dbContext.ProfileRevisions.Add(revision);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task GivenASavegamePlayedOnTheFirstRevision(RepoId repoId, ProfileId profileId)
    {
        using var dbContext = fixture.CreateDbContext();

        var savegame = new Savegame(repoId, new SavegameName($"save-{Guid.NewGuid()}"), profileId, DateTime.UtcNow);

        dbContext.Savegames.Add(savegame);
        dbContext.SavegameVersions.Add(savegame.CreateVersion(
            profileId,
            new RevisionNumber(1),
            new string('1', ModImageHash.Length),
            sizeBytes: 1024,
            _author,
            DateTime.UtcNow));
        dbContext.SavegameCheckouts.Add(new SavegameCheckout(repoId, savegame.Id, _author, DateTime.UtcNow));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task GivenAnInvite(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        dbContext.RepoInvites.Add(new RepoInvite(
            repoId,
            InviteCodes.Generate(),
            RepoMembershipLevel.Member,
            _author,
            DateTime.UtcNow,
            expiresAt: null,
            maximumUses: null));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Exactly what <c>DeleteRepoV1Endpoint</c> does once it has decided the repo may go: empty it in
    /// the order the foreign keys force, then drop the row, in one transaction.
    /// </summary>
    private async Task GivenTheRepoIsDeleted(RepoId repoId)
    {
        using var dbContext = fixture.CreateDbContext();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken.None);

        await dbContext.EmptyAsync(repoId, CancellationToken.None);

        dbContext.Repos.Remove((await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await transaction.CommitAsync(CancellationToken.None);
    }

    private static ModVersion CreateVersion(RepoId repoId, string versionId, int sequenceNumber) => new()
    {
        RepoId = repoId,
        ModId = _modId,
        Id = new ModVersionId(versionId),
        SequenceNumber = sequenceNumber,
        DisplayName = versionId,
        Description = "",
        FileName = $"{_modId.Value}.zip",
        ContentHash = versionId,
        Locked = false,
        Attributes = [],
        Created = _timestamp,
        Updated = _timestamp
    };
}
