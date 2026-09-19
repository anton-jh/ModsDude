using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The two statements the size backfill is made of. Provider questions rather than model ones: one is
/// a projection into a value-object address, the other an <c>ExecuteUpdate</c> keyed on three
/// strongly-typed ids, and neither is something an in-memory substitute would translate the way
/// PostgreSQL does.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class ModVersionSizeTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset _timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task A_version_registered_without_a_size_is_listed_until_it_is_given_one()
    {
        var address = await GivenAVersionWithSize(null);

        using (var dbContext = fixture.CreateDbContext())
        {
            Assert.Contains(address, await dbContext.ModVersions.GetAddressesWithoutSizeAsync(CancellationToken.None));

            Assert.Equal(1, await dbContext.ModVersions.RecordSizeAsync(address, 4096, CancellationToken.None));
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            Assert.DoesNotContain(address, await dbContext.ModVersions.GetAddressesWithoutSizeAsync(CancellationToken.None));

            var stored = await dbContext.ModVersions.GetAsync(address.RepoId, address.ModId, address.VersionId, CancellationToken.None);

            Assert.Equal(4096, stored!.SizeBytes);
        }
    }

    /// <summary>
    /// The delta form of the mod list is keyed on <c>Updated</c>, so restamping every old version for a
    /// column that arrived late would make the first delta after a deploy as big as the whole list.
    /// </summary>
    [Fact]
    public async Task Recording_a_size_leaves_the_version_as_it_was()
    {
        var address = await GivenAVersionWithSize(null);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.ModVersions.RecordSizeAsync(address, 4096, CancellationToken.None);
        }

        using var readContext = fixture.CreateDbContext();

        var stored = await readContext.ModVersions.GetAsync(address.RepoId, address.ModId, address.VersionId, CancellationToken.None);

        Assert.Equal(_timestamp, stored!.Updated);
    }

    [Fact]
    public async Task A_version_that_already_has_a_size_is_not_listed()
    {
        var address = await GivenAVersionWithSize(2048);

        using var dbContext = fixture.CreateDbContext();

        Assert.DoesNotContain(address, await dbContext.ModVersions.GetAddressesWithoutSizeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Recording_a_size_for_a_version_that_is_gone_writes_nothing()
    {
        using var dbContext = fixture.CreateDbContext();

        var address = new ModBlobAddress(new RepoId(Guid.NewGuid()), new ModId("gone"), new ModVersionId("1.0.0"));

        Assert.Equal(0, await dbContext.ModVersions.RecordSizeAsync(address, 1, CancellationToken.None));
    }


    private async Task<ModBlobAddress> GivenAVersionWithSize(long? sizeBytes)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var repo = new Repo(new RepoName($"repo-{Guid.NewGuid()}"), DateTime.UtcNow, userId)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        var version = new ModVersion
        {
            RepoId = repo.Id,
            ModId = new ModId("a_mod"),
            Id = new ModVersionId("1.0.0"),
            SequenceNumber = 0,
            DisplayName = "A mod",
            Description = "",
            FileName = "a_mod.zip",
            ContentHash = "hash",
            SizeBytes = sizeBytes,
            Locked = false,
            Attributes = [],
            Created = _timestamp,
            Updated = _timestamp
        };

        dbContext.Users.Add(new User(userId, new DisplayName(userId.Value), DateTime.UtcNow));
        dbContext.Repos.Add(repo);
        dbContext.ModVersions.Add(version);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return new ModBlobAddress(repo.Id, version.ModId, version.Id);
    }
}
