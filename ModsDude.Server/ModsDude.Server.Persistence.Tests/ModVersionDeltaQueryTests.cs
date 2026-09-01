using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The shape the mod list's delta form issues: filter a repo by <c>Updated</c>, then order by it and
/// break ties on the strongly-typed ids. Ordering on a value-converted property is the provider's
/// decision to translate or refuse, and a refusal would only surface as a request-time exception, so
/// it is asserted here rather than trusted.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ModVersionDeltaQueryTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _early = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _late = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task Versions_order_by_update_time_and_break_ties_on_their_ids()
    {
        var repoId = await GivenVersions(
            ("b_mod", "2.0.0", _early),
            ("b_mod", "1.0.0", _early),
            ("a_mod", "1.0.0", _early),
            ("a_mod", "1.0.0-later", _late));

        using var dbContext = fixture.CreateDbContext();

        var ordered = await dbContext.ModVersions
            .Where(x => x.RepoId == repoId)
            .OrderBy(x => x.Updated).ThenBy(x => x.ModId).ThenBy(x => x.Id)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(
            [("a_mod", "1.0.0"), ("b_mod", "1.0.0"), ("b_mod", "2.0.0"), ("a_mod", "1.0.0-later")],
            ordered.Select(x => (x.ModId.Value, x.Id.Value)));
    }

    [Fact]
    public async Task A_delta_returns_only_what_changed_after_the_given_moment()
    {
        var repoId = await GivenVersions(
            ("a_mod", "1.0.0", _early),
            ("a_mod", "2.0.0", _late));

        using var dbContext = fixture.CreateDbContext();

        var changed = await dbContext.ModVersions
            .Where(x => x.RepoId == repoId && x.Updated > _early)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(["2.0.0"], changed.Select(x => x.Id.Value));
    }

    /// <summary>
    /// One registration stamps every sibling it shifts with the same timestamp, so a page can end
    /// inside a run of equal <c>Updated</c> values. The cursor resumes from that timestamp and skips
    /// what it already handed out, which only works if the order is total and stable.
    /// </summary>
    [Fact]
    public async Task Resuming_from_a_timestamp_and_skipping_what_was_taken_returns_the_rest_of_the_run()
    {
        var repoId = await GivenVersions(
            ("a_mod", "1.0.0", _early),
            ("a_mod", "2.0.0", _early),
            ("a_mod", "3.0.0", _early));

        using var dbContext = fixture.CreateDbContext();

        var resumed = await dbContext.ModVersions
            .Where(x => x.RepoId == repoId && x.Updated >= _early)
            .OrderBy(x => x.Updated).ThenBy(x => x.ModId).ThenBy(x => x.Id)
            .Skip(2)
            .Take(2)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(["3.0.0"], resumed.Select(x => x.Id.Value));
    }


    private async Task<RepoId> GivenVersions(params (string ModId, string VersionId, DateTimeOffset Updated)[] versions)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);

        foreach (var group in versions.GroupBy(x => x.ModId))
        {
            dbContext.ModVersions.AddRange(group.Select((version, index) => new ModVersion
            {
                RepoId = repo.Id,
                ModId = new ModId(version.ModId),
                Id = new ModVersionId(version.VersionId),
                SequenceNumber = index,
                DisplayName = version.VersionId,
                Description = "",
                ContentHash = version.VersionId,
                Locked = false,
                Attributes = [],
                Created = _early,
                Updated = version.Updated
            }));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }
}
