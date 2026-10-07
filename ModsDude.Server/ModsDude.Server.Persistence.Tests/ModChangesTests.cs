using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Changes;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The mod list's feed of changes, which the database numbers itself: see <see cref="RepoChangeCounter"/>.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ModChangesTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task A_first_read_returns_every_version_and_where_the_repo_stands()
    {
        var repoId = await GivenRepo();
        await Register(repoId, ("a_mod", "1.0.0"), ("a_mod", "2.0.0"), ("b_mod", "1.0.0"));

        var changes = await Read(repoId, after: 0);

        Assert.Equal([("a_mod", "1.0.0"), ("a_mod", "2.0.0"), ("b_mod", "1.0.0")], Ids(changes.Versions));
        Assert.Equal(3, changes.Sequence);
        Assert.False(changes.HasMore);
    }

    [Fact]
    public async Task A_later_read_returns_only_what_changed_since()
    {
        var repoId = await GivenRepo();
        await Register(repoId, ("a_mod", "1.0.0"), ("b_mod", "1.0.0"));
        var first = await Read(repoId, after: 0);

        await Change(repoId, "a_mod", "1.0.0", x => x.DisplayName = "Renamed");

        var second = await Read(repoId, first.Sequence);

        var changed = Assert.Single(second.Versions);
        Assert.Equal("Renamed", changed.DisplayName);
        Assert.Equal(first.Sequence + 1, second.Sequence);
    }

    [Fact]
    public async Task A_deleted_version_is_logged_however_it_was_deleted()
    {
        var repoId = await GivenRepo();
        await Register(repoId, ("a_mod", "1.0.0"), ("a_mod", "2.0.0"));
        var first = await Read(repoId, after: 0);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.ModVersions
                .Where(x => x.RepoId == repoId && x.Id == new ModVersionId("1.0.0"))
                .ExecuteDeleteAsync(CancellationToken.None);
        }

        var second = await Read(repoId, first.Sequence);

        Assert.Empty(second.Versions);
        var deleted = Assert.Single(second.Deleted);
        Assert.Equal(("a_mod", "1.0.0"), (deleted.ModId.Value, deleted.VersionId.Value));
    }

    [Fact]
    public async Task A_bulk_update_numbers_every_row_it_touches()
    {
        var repoId = await GivenRepo();
        await Register(repoId, ("a_mod", "1.0.0"), ("a_mod", "2.0.0"));
        var first = await Read(repoId, after: 0);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.ModVersions
                .Where(x => x.RepoId == repoId)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.Locked, true), CancellationToken.None);
        }

        var second = await Read(repoId, first.Sequence);

        Assert.Equal(2, second.Versions.Count);
        Assert.All(second.Versions, x => Assert.True(x.Locked));
    }

    [Fact]
    public async Task Paging_through_a_long_list_misses_nothing()
    {
        var repoId = await GivenRepo();
        await Register(repoId, [.. Enumerable.Range(1, 7).Select(i => ("a_mod", $"{i}.0.0"))]);

        var seen = new List<(string, string)>();
        long after = 0;
        ModChanges page;

        do
        {
            page = await Read(repoId, after, limit: 3);
            seen.AddRange(Ids(page.Versions));
            after = page.Sequence;
        }
        while (page.HasMore);

        Assert.Equal(7, seen.Distinct().Count());
        Assert.Equal(7, after);
    }

    [Fact]
    public async Task Repos_are_numbered_apart()
    {
        var first = await GivenRepo();
        var second = await GivenRepo();
        await Register(first, ("a_mod", "1.0.0"), ("a_mod", "2.0.0"));
        await Register(second, ("b_mod", "1.0.0"));

        Assert.Equal(2, (await Read(first, 0)).Sequence);
        Assert.Equal(1, (await Read(second, 0)).Sequence);
    }

    /// <summary>
    /// The property everything rests on: a change still uncommitted holds the counter, so no reader can
    /// be told it has seen up to a number while a change below that number is still on its way.
    /// </summary>
    [Fact]
    public async Task A_change_not_yet_committed_is_neither_returned_nor_counted()
    {
        var repoId = await GivenRepo();
        await Register(repoId, ("a_mod", "1.0.0"));

        using var writer = fixture.CreateDbContext();
        await using var transaction = await writer.Database.BeginTransactionAsync(CancellationToken.None);
        writer.ModVersions.Add(Version(repoId, "a_mod", "2.0.0", 1));
        await writer.SaveChangesAsync(CancellationToken.None);

        var during = await Read(repoId, after: 0);

        await transaction.CommitAsync(CancellationToken.None);

        var after = await Read(repoId, during.Sequence);

        Assert.Equal(1, during.Sequence);
        Assert.Equal([("a_mod", "1.0.0")], Ids(during.Versions));
        Assert.Equal([("a_mod", "2.0.0")], Ids(after.Versions));
    }

    [Fact]
    public async Task A_version_deleted_and_registered_again_is_listed_as_existing()
    {
        var repoId = await GivenRepo();
        await Register(repoId, ("a_mod", "1.0.0"));
        var first = await Read(repoId, after: 0);

        using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.ModVersions.Remove(await dbContext.ModVersions.SingleAsync(x => x.RepoId == repoId));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await Register(repoId, ("a_mod", "1.0.0"));

        var second = await Read(repoId, first.Sequence);

        Assert.Single(second.Deleted);
        Assert.Equal([("a_mod", "1.0.0")], Ids(second.Versions));
    }


    private async Task<ModChanges> Read(RepoId repoId, long after, int limit = 100)
    {
        using var dbContext = fixture.CreateDbContext();

        return await ModChanges.ReadAsync(dbContext, repoId, after, limit, CancellationToken.None);
    }

    private async Task Change(RepoId repoId, string modId, string versionId, Action<ModVersion> change)
    {
        using var dbContext = fixture.CreateDbContext();

        var version = await dbContext.ModVersions.SingleAsync(
            x => x.RepoId == repoId && x.ModId == new ModId(modId) && x.Id == new ModVersionId(versionId));

        change(version);

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task Register(RepoId repoId, params (string ModId, string VersionId)[] versions)
    {
        using var dbContext = fixture.CreateDbContext();

        var taken = await dbContext.ModVersions.Where(x => x.RepoId == repoId).CountAsync(CancellationToken.None);

        dbContext.ModVersions.AddRange(versions.Select((x, i) => Version(repoId, x.ModId, x.VersionId, taken + i)));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<RepoId> GivenRepo()
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var user = new User(userId, new DisplayName(userId.Value), DateTime.UtcNow);
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, user)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(user);
        dbContext.Repos.Add(repo);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }

    private static ModVersion Version(RepoId repoId, string modId, string versionId, int sequenceNumber) => new()
    {
        RepoId = repoId,
        ModId = new ModId(modId),
        Id = new ModVersionId(versionId),
        SequenceNumber = sequenceNumber,
        DisplayName = versionId,
        Description = "",
        FileName = $"{modId}.zip",
        ContentHash = $"{modId}-{versionId}",
        SizeBytes = 1024,
        Locked = false,
        Attributes = [],
        Created = _now,
        Updated = _now
    };

    private static List<(string, string)> Ids(IEnumerable<ModVersion> versions)
        => [.. versions.Select(x => (x.ModId.Value, x.Id.Value))];
}
