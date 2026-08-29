using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// Inserting a version renumbers a whole run of siblings through sequence numbers their neighbours
/// still hold, and the unique index on <c>(RepoId, ModId, SequenceNumber)</c> is checked per row.
/// The shift only survives because EF orders the batch's updates so that no row transiently
/// collides, which it can do because the index is declared unique <em>in the model</em>. That is a
/// provider decision no test double reproduces, so it is asserted against a real PostgreSQL: should
/// the index stop being part of the model, or that ordering guarantee change, these fail.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ModVersionOrderingTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task Inserting_before_the_first_version_shifts_every_sibling_up_without_violating_the_unique_ordering()
    {
        // Lexically 1.10.0 sorts before 1.9.0, so the version ids run the opposite way to the
        // sequence numbers: nothing about the ids can be what keeps the update order safe.
        var (repoId, modId) = await GivenAMod("1.9.0", "1.10.0", "1.11.0");

        await WhenAVersionIsInserted(repoId, modId, "1.8.0", after: null, before: "1.9.0");

        await ThenTheOrderingIs(repoId, modId, "1.8.0", "1.9.0", "1.10.0", "1.11.0");
    }

    [Fact]
    public async Task Inserting_between_two_versions_shifts_only_the_versions_after_it()
    {
        var (repoId, modId) = await GivenAMod("1.9.0", "1.10.0", "1.11.0");

        await WhenAVersionIsInserted(repoId, modId, "1.10.5", after: "1.10.0", before: "1.11.0");

        await ThenTheOrderingIs(repoId, modId, "1.9.0", "1.10.0", "1.10.5", "1.11.0");
    }

    [Fact]
    public async Task Inserting_before_the_first_of_many_versions_stays_contiguous_across_batch_boundaries()
    {
        // Past the provider's maximum batch size, so the shift is split over several round trips and
        // the ordering has to hold between them as well as within them.
        var versionIds = Enumerable.Range(0, 1200).Select(_ => Guid.NewGuid().ToString()).ToArray();

        var (repoId, modId) = await GivenAMod(versionIds);

        await WhenAVersionIsInserted(repoId, modId, "first", after: null, before: versionIds[0]);

        await ThenTheOrderingIs(repoId, modId, [.. versionIds.Prepend("first")]);
    }

    [Fact]
    public async Task Removing_a_version_closes_the_gap_it_leaves_without_violating_the_unique_ordering()
    {
        var (repoId, modId) = await GivenAMod("1.9.0", "1.10.0", "1.11.0");

        using (var dbContext = fixture.CreateDbContext())
        {
            var siblings = await dbContext.ModVersions.GetVersionsOfModAsync(repoId, modId, CancellationToken.None);
            var removed = siblings.Single(x => x.Id == new ModVersionId("1.9.0"));

            dbContext.ModVersions.Remove(removed);
            ModVersionSequencer.CloseGap([.. siblings.Where(x => x != removed)], removed, _timestamp);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await ThenTheOrderingIs(repoId, modId, "1.10.0", "1.11.0");
    }


    private async Task<(RepoId RepoId, ModId ModId)> GivenAMod(params string[] versionIds)
    {
        var modId = new ModId("FS25_TestMod");

        using var dbContext = fixture.CreateDbContext();

        // Every test gets its own repo, so nothing here depends on the order the suite runs in.
        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(new User(userId, new Username(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.ModVersions.AddRange(versionIds.Select((versionId, index) => CreateVersion(repo.Id, modId, versionId, index)));

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, modId);
    }

    private async Task WhenAVersionIsInserted(RepoId repoId, ModId modId, string versionId, string? after, string? before)
    {
        using var dbContext = fixture.CreateDbContext();

        var siblings = await dbContext.ModVersions.GetVersionsOfModAsync(repoId, modId, CancellationToken.None);

        var sequenceNumber = ModVersionSequencer.MakeRoomAt(
            siblings,
            after is null ? null : new ModVersionId(after),
            before is null ? null : new ModVersionId(before),
            _timestamp);

        dbContext.ModVersions.Add(CreateVersion(repoId, modId, versionId, sequenceNumber));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ThenTheOrderingIs(RepoId repoId, ModId modId, params string[] expectedVersionIds)
    {
        using var dbContext = fixture.CreateDbContext();

        var ordered = await dbContext.ModVersions
            .Where(x => x.RepoId == repoId && x.ModId == modId)
            .OrderBy(x => x.SequenceNumber)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(expectedVersionIds, ordered.Select(x => x.Id.Value));
        Assert.Equal(Enumerable.Range(0, expectedVersionIds.Length), ordered.Select(x => x.SequenceNumber));
    }

    private static ModVersion CreateVersion(RepoId repoId, ModId modId, string versionId, int sequenceNumber) => new()
    {
        RepoId = repoId,
        ModId = modId,
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
