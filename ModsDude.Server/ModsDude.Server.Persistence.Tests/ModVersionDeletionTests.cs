using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// What the delete endpoints ask the database before removing a version, and what the database does
/// if they were somehow not asked. Both are provider questions: the usage query reaches a mod
/// version through a profile's owned dependency collection, and the refusal is a foreign key
/// declared Restrict rather than the cascade EF would otherwise infer.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ModVersionDeletionTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ModId _modId = new("FS25_TestMod");


    [Fact]
    public async Task A_version_a_profile_pins_is_reported_as_depended_on()
    {
        var repoId = await GivenAModWithVersions("1.0.0", "2.0.0");
        await GivenAProfilePinning(repoId, "1.0.0");

        using var dbContext = fixture.CreateDbContext();

        Assert.True(await dbContext.Profiles.CheckIfVersionIsDependedOn(repoId, _modId, new ModVersionId("1.0.0"), CancellationToken.None));
    }

    [Fact]
    public async Task A_version_no_profile_pins_is_not_reported_as_depended_on()
    {
        var repoId = await GivenAModWithVersions("1.0.0", "2.0.0");
        await GivenAProfilePinning(repoId, "1.0.0");

        using var dbContext = fixture.CreateDbContext();

        Assert.False(await dbContext.Profiles.CheckIfVersionIsDependedOn(repoId, _modId, new ModVersionId("2.0.0"), CancellationToken.None));
    }

    [Fact]
    public async Task A_mod_is_reported_as_depended_on_whichever_of_its_versions_is_pinned()
    {
        var repoId = await GivenAModWithVersions("1.0.0", "2.0.0");
        await GivenAProfilePinning(repoId, "2.0.0");

        using var dbContext = fixture.CreateDbContext();

        Assert.True(await dbContext.Profiles.CheckIfModIsDependedOn(repoId, _modId, CancellationToken.None));
    }

    [Fact]
    public async Task A_mod_nothing_pins_is_not_reported_as_depended_on()
    {
        var repoId = await GivenAModWithVersions("1.0.0");

        using var dbContext = fixture.CreateDbContext();

        Assert.False(await dbContext.Profiles.CheckIfModIsDependedOn(repoId, _modId, CancellationToken.None));
    }

    /// <summary>
    /// The endpoints refuse this case themselves; the constraint is what stops a dependency added
    /// between that check and the commit from being swept away without anyone asking.
    /// </summary>
    [Fact]
    public async Task Deleting_a_pinned_version_is_refused_by_the_database()
    {
        var repoId = await GivenAModWithVersions("1.0.0", "2.0.0");
        await GivenAProfilePinning(repoId, "1.0.0");

        using var dbContext = fixture.CreateDbContext();

        var version = await dbContext.ModVersions.GetAsync(repoId, _modId, new ModVersionId("1.0.0"), CancellationToken.None);
        dbContext.ModVersions.Remove(version!);

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_an_unpinned_version_is_allowed()
    {
        var repoId = await GivenAModWithVersions("1.0.0", "2.0.0");
        await GivenAProfilePinning(repoId, "1.0.0");

        using (var dbContext = fixture.CreateDbContext())
        {
            var siblings = await dbContext.ModVersions.GetVersionsOfModAsync(repoId, _modId, CancellationToken.None);
            var removed = siblings.Single(x => x.Id == new ModVersionId("2.0.0"));

            dbContext.ModVersions.Remove(removed);
            ModVersionSequencer.CloseGap([.. siblings.Where(x => x != removed)], removed, _timestamp);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();
        var remaining = await verification.ModVersions.GetVersionsOfModAsync(repoId, _modId, CancellationToken.None);

        Assert.Equal(["1.0.0"], remaining.Select(x => x.Id.Value));
    }


    private async Task<RepoId> GivenAModWithVersions(params string[] versionIds)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.ModVersions.AddRange(versionIds.Select((versionId, index) => CreateVersion(repo.Id, versionId, index)));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    private async Task GivenAProfilePinning(RepoId repoId, string versionId)
    {
        using var dbContext = fixture.CreateDbContext();

        var version = await dbContext.ModVersions.GetAsync(repoId, _modId, new ModVersionId(versionId), CancellationToken.None);
        var profile = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);

        profile.AddDependency(version!, locked: false);
        dbContext.Profiles.Add(profile);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ModVersion CreateVersion(RepoId repoId, string versionId, int sequenceNumber) => new()
    {
        RepoId = repoId,
        ModId = _modId,
        Id = new ModVersionId(versionId),
        SequenceNumber = sequenceNumber,
        DisplayName = versionId,
        Description = "",
        ContentHash = versionId,
        Locked = false,
        Attributes = [],
        Created = _timestamp,
        Updated = _timestamp
    };
}
