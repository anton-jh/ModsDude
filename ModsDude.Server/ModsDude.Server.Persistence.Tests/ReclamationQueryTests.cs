using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The two queries the blob reclamation sweep asks the database for: every registered
/// <c>(repoId, modId, versionId)</c> triple, and every image hash any version still references.
/// </summary>
/// <remarks>
/// They are asserted against a real database rather than trusted because both are shapes the provider
/// may refuse to translate — a projection of value-converted ids, and a <c>SelectMany</c> across an
/// owned collection — and a refusal surfaces only when the query runs. The sweep runs unattended on a
/// timer, so a failure there is a failure nobody is watching, and one that leaves storage growing
/// exactly as it did before the sweep existed.
/// </remarks>
[Collection(nameof(DatabaseCollection))]
public class ReclamationQueryTests(DatabaseFixture fixture)
{
    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string _otherHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static readonly DateTimeOffset _timestamp = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task Every_registered_version_projects_to_the_address_its_blob_is_stored_at()
    {
        var repoId = await GivenVersions(("a_mod", "1.0.0", []), ("a_mod", "2.0.0", []), ("b_mod", "1.0.0", []));

        using var dbContext = fixture.CreateDbContext();

        var registered = (await dbContext.ModVersions
            .AsNoTracking()
            .Where(x => x.RepoId == repoId)
            .Select(x => new { x.RepoId, x.ModId, x.Id })
            .ToListAsync(CancellationToken.None))
            .Select(x => new ModBlobAddress(x.RepoId, x.ModId, x.Id))
            .ToHashSet();

        Assert.Equal(3, registered.Count);
        Assert.Contains(new ModBlobAddress(repoId, new ModId("a_mod"), new ModVersionId("2.0.0")), registered);
        Assert.DoesNotContain(new ModBlobAddress(repoId, new ModId("b_mod"), new ModVersionId("2.0.0")), registered);
    }

    [Fact]
    public async Task Image_references_read_back_deduplicated_across_versions()
    {
        var repoId = await GivenVersions(
            ("a_mod", "1.0.0", [_hash]),
            ("a_mod", "2.0.0", [_hash, _otherHash]));

        using var dbContext = fixture.CreateDbContext();

        var referenced = await dbContext.ModVersions
            .AsNoTracking()
            .Where(x => x.RepoId == repoId)
            .SelectMany(x => x.Images)
            .Select(x => x.Hash)
            .Distinct()
            .ToListAsync(CancellationToken.None);

        Assert.Equal([_otherHash, _hash], referenced.Order());
    }

    [Fact]
    public async Task A_version_carrying_no_images_contributes_no_references()
    {
        var repoId = await GivenVersions(("a_mod", "1.0.0", []));

        using var dbContext = fixture.CreateDbContext();

        var referenced = await dbContext.ModVersions
            .AsNoTracking()
            .Where(x => x.RepoId == repoId)
            .SelectMany(x => x.Images)
            .Select(x => x.Hash)
            .ToListAsync(CancellationToken.None);

        Assert.Empty(referenced);
    }


    private async Task<RepoId> GivenVersions(params (string ModId, string VersionId, string[] ImageHashes)[] versions)
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
            foreach (var (version, index) in group.Select((x, i) => (x, i)))
            {
                var modVersion = new ModVersion
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
                    Created = _timestamp,
                    Updated = _timestamp
                };

                modVersion.SetImages(
                    [.. version.ImageHashes.Select((hash, position) =>
                        new ModImageReference(hash, ModImageKind.StoreImage, ModImageRendition.Full, position, $"{position}.png"))],
                    _timestamp);

                dbContext.ModVersions.Add(modVersion);
            }
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return repo.Id;
    }
}
