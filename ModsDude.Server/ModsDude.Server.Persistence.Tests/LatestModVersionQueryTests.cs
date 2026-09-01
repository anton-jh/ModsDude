using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The query behind "apply all updates". A provider question rather than a model one: it filters on
/// a collection of value-object ids and correlates the table against itself to pick the row no
/// sibling sits after, neither of which an in-memory substitute would answer for PostgreSQL.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class LatestModVersionQueryTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task The_latest_version_of_each_named_mod_comes_back_and_nothing_else()
    {
        var repoId = await GivenARepo();

        await GivenAMod(repoId, "A", "1.0.0", "2.0.0", "3.0.0");
        await GivenAMod(repoId, "B", "1.0.0", "1.1.0");
        await GivenAMod(repoId, "C", "1.0.0");

        using var dbContext = fixture.CreateDbContext();

        var latest = await dbContext.ModVersions.GetLatestVersionOfEachAsync(
            repoId,
            [new ModId("A"), new ModId("B")],
            CancellationToken.None);

        Assert.Equal(
            [("A", "3.0.0"), ("B", "1.1.0")],
            latest.Values.OrderBy(x => x.ModId.Value).Select(x => (x.ModId.Value, x.Id.Value)));
    }

    [Fact]
    public async Task The_latest_version_is_the_last_in_sequence_rather_than_the_last_registered()
    {
        // The version ids run the opposite way to the sequence numbers, so nothing about the ids or
        // the insertion order can be what decides the answer.
        var repoId = await GivenARepo();

        await GivenAMod(repoId, "A", "3.0.0", "1.0.0", "2.0.0");

        using var dbContext = fixture.CreateDbContext();

        var latest = await dbContext.ModVersions.GetLatestVersionOfEachAsync(repoId, [new ModId("A")], CancellationToken.None);

        Assert.Equal("2.0.0", latest[new ModId("A")].Id.Value);
    }

    [Fact]
    public async Task A_mod_of_the_same_id_in_another_repo_does_not_answer_for_this_one()
    {
        var repoId = await GivenARepo();
        var otherRepoId = await GivenARepo();

        await GivenAMod(repoId, "A", "1.0.0");
        await GivenAMod(otherRepoId, "A", "1.0.0", "9.0.0");

        using var dbContext = fixture.CreateDbContext();

        var latest = await dbContext.ModVersions.GetLatestVersionOfEachAsync(repoId, [new ModId("A")], CancellationToken.None);

        Assert.Equal("1.0.0", latest[new ModId("A")].Id.Value);
    }

    [Fact]
    public async Task A_mod_with_no_versions_left_is_simply_absent()
    {
        var repoId = await GivenARepo();

        using var dbContext = fixture.CreateDbContext();

        var latest = await dbContext.ModVersions.GetLatestVersionOfEachAsync(repoId, [new ModId("A")], CancellationToken.None);

        Assert.Empty(latest);
    }


    private async Task<RepoId> GivenARepo()
    {
        using var dbContext = fixture.CreateDbContext();

        // Every test gets its own repo, so nothing here depends on the order the suite runs in.
        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    private async Task GivenAMod(RepoId repoId, string modId, params string[] versionIdsInOrder)
    {
        using var dbContext = fixture.CreateDbContext();

        dbContext.ModVersions.AddRange(versionIdsInOrder.Select((versionId, index) => new ModVersion()
        {
            RepoId = repoId,
            ModId = new ModId(modId),
            Id = new ModVersionId(versionId),
            SequenceNumber = index,
            DisplayName = versionId,
            Description = "",
            ContentHash = versionId,
            Locked = false,
            Attributes = [],
            Created = _timestamp,
            Updated = _timestamp
        }));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
