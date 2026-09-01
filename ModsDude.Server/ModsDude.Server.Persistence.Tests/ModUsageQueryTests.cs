using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The query behind the Manage page's "Unused" filter. A provider question rather than a model one:
/// it groups an owned collection reached through its owner, projects value-object keys out of the
/// group, and windows the result — none of which an in-memory substitute would answer for
/// PostgreSQL.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ModUsageQueryTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task A_version_no_profile_pins_is_absent_rather_than_zero()
    {
        // Absence is the encoding, which is what keeps the response proportional to what is used
        // rather than to the size of the catalog.
        var repoId = await GivenARepoWithAMod("A", "1.0.0", "2.0.0");
        await GivenAProfilePinning(repoId, ("A", "1.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var usage = await dbContext.Profiles.GetModUsageAsync(repoId, 0, 100, CancellationToken.None);

        Assert.Equal(["1.0.0"], usage.Select(x => x.VersionId.Value));
    }

    [Fact]
    public async Task Every_profile_pinning_a_version_is_counted_once()
    {
        var repoId = await GivenARepoWithAMod("A", "1.0.0");
        await GivenAProfilePinning(repoId, ("A", "1.0.0"));
        await GivenAProfilePinning(repoId, ("A", "1.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var usage = await dbContext.Profiles.GetModUsageAsync(repoId, 0, 100, CancellationToken.None);

        Assert.Equal(2, Assert.Single(usage).ProfileCount);
    }

    [Fact]
    public async Task Usage_is_scoped_to_the_repo_that_was_asked_about()
    {
        var repoId = await GivenARepoWithAMod("A", "1.0.0");
        var otherRepoId = await GivenARepoWithAMod("A", "1.0.0");

        await GivenAProfilePinning(otherRepoId, ("A", "1.0.0"));

        using var dbContext = fixture.CreateDbContext();

        Assert.Empty(await dbContext.Profiles.GetModUsageAsync(repoId, 0, 100, CancellationToken.None));
    }

    [Fact]
    public async Task The_listing_is_ordered_and_windowed_so_it_can_be_paged()
    {
        var repoId = await GivenARepoWithAMod("A", "1.0.0", "2.0.0");
        await GivenAMod(repoId, "B", "1.0.0");

        await GivenAProfilePinning(repoId, ("A", "1.0.0"), ("B", "1.0.0"));
        await GivenAProfilePinning(repoId, ("A", "2.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var first = await dbContext.Profiles.GetModUsageAsync(repoId, 0, 2, CancellationToken.None);
        var second = await dbContext.Profiles.GetModUsageAsync(repoId, 2, 2, CancellationToken.None);

        Assert.Equal([("A", "1.0.0"), ("A", "2.0.0")], first.Select(x => (x.ModId.Value, x.VersionId.Value)));
        Assert.Equal([("B", "1.0.0")], second.Select(x => (x.ModId.Value, x.VersionId.Value)));
    }


    private async Task<RepoId> GivenARepoWithAMod(string modId, params string[] versionIds)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.ModVersions.AddRange(versionIds.Select((versionId, index) => CreateVersion(repo.Id, modId, versionId, index)));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    private async Task GivenAMod(RepoId repoId, string modId, params string[] versionIds)
    {
        using var dbContext = fixture.CreateDbContext();

        dbContext.ModVersions.AddRange(versionIds.Select((versionId, index) => CreateVersion(repoId, modId, versionId, index)));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task GivenAProfilePinning(RepoId repoId, params (string ModId, string VersionId)[] pinned)
    {
        using var dbContext = fixture.CreateDbContext();

        var profile = new Profile(repoId, new ProfileName($"profile-{Guid.NewGuid()}"), DateTime.UtcNow);

        foreach (var (modId, versionId) in pinned)
        {
            var version = await dbContext.ModVersions.GetAsync(repoId, new ModId(modId), new ModVersionId(versionId), CancellationToken.None);

            profile.AddDependency(version!, locked: false);
        }

        dbContext.Profiles.Add(profile);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ModVersion CreateVersion(RepoId repoId, string modId, string versionId, int sequenceNumber) => new()
    {
        RepoId = repoId,
        ModId = new ModId(modId),
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
