using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Storage;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The names the database refers to, which the audit compares storage with and the deletion run asks
/// about before every delete. Against a real database, because the projections are shapes a provider
/// may refuse to translate, and a refusal surfaces only when the query runs.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class BlobReferencesTests(DatabaseFixture fixture)
{
    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string _otherHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static readonly DateTimeOffset _timestamp = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);


    [Fact]
    public async Task Every_registered_version_is_referred_to_by_the_name_its_blob_is_stored_under()
    {
        var repoId = await GivenVersions(("a_mod", "1.0.0", []), ("a_mod", "2.0.0", []), ("b_mod", "1.0.0", []));

        using var dbContext = fixture.CreateDbContext();

        var referenced = await dbContext.GetReferencedNamesAsync(StorageContainer.Mods, CancellationToken.None);

        Assert.Contains(BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("2.0.0")), referenced);
        Assert.DoesNotContain(BlobNames.Mod(repoId, new ModId("b_mod"), new ModVersionId("2.0.0")), referenced);
    }

    [Fact]
    public async Task Images_are_referred_to_by_versions_and_by_avatars()
    {
        await GivenVersions(("a_mod", "1.0.0", [_hash]), ("a_mod", "2.0.0", [_hash]));
        var avatarHash = TestHashes.Of($"avatar-{Guid.NewGuid()}");
        await GivenAUserWithAvatar(avatarHash);

        using var dbContext = fixture.CreateDbContext();

        var referenced = await dbContext.GetReferencedNamesAsync(StorageContainer.Images, CancellationToken.None);

        Assert.Contains(BlobNames.Image(_hash), referenced);
        Assert.Contains(BlobNames.Image(avatarHash), referenced);
    }

    [Fact]
    public async Task One_blob_is_asked_about_on_its_own()
    {
        var repoId = await GivenVersions(("a_mod", "1.0.0", [_otherHash]));
        var avatarHash = TestHashes.Of($"avatar-{Guid.NewGuid()}");
        await GivenAUserWithAvatar(avatarHash);

        using var dbContext = fixture.CreateDbContext();

        Assert.True(await dbContext.IsReferencedAsync(StorageContainer.Mods, BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1.0.0")), CancellationToken.None));
        Assert.False(await dbContext.IsReferencedAsync(StorageContainer.Mods, BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("9")), CancellationToken.None));
        Assert.True(await dbContext.IsReferencedAsync(StorageContainer.Images, BlobNames.Image(_otherHash), CancellationToken.None));
        Assert.True(await dbContext.IsReferencedAsync(StorageContainer.Images, BlobNames.Image(avatarHash), CancellationToken.None));
        Assert.False(await dbContext.IsReferencedAsync(StorageContainer.Images, BlobNames.Image(TestHashes.Of("nobody")), CancellationToken.None));
    }

    [Fact]
    public async Task A_name_that_does_not_parse_is_treated_as_referred_to()
    {
        using var dbContext = fixture.CreateDbContext();

        Assert.True(await dbContext.IsReferencedAsync(StorageContainer.Mods, "stray.txt", CancellationToken.None));
    }

    [Fact]
    public async Task A_mod_blob_is_labelled_from_its_version_and_repo()
    {
        var repoId = await GivenVersions(("a_mod", "1.0.0", []));
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1.0.0"));

        using var dbContext = fixture.CreateDbContext();

        var labels = await dbContext.GetLabelsAsync(StorageContainer.Mods, [name, BlobNames.Mod(repoId, new ModId("gone"), new ModVersionId("1"))], CancellationToken.None);

        var label = Assert.IsType<ModBlobLabel>(Assert.Single(labels).Value);
        Assert.Equal(("a_mod.zip", "1.0.0", "_test@1"), (label.FileName, label.VersionId.Value, label.Game.Value));
        Assert.Null(label.AddedBy);
    }


    private async Task<RepoId> GivenVersions(params (string ModId, string VersionId, string[] ImageHashes)[] versions)
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
                    FileName = $"{version.ModId}.zip",
                    ContentHash = TestHashes.Of(version.VersionId),
                    SizeBytes = 1024,
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

    private async Task GivenAUserWithAvatar(string hash)
    {
        using var dbContext = fixture.CreateDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var user = new User(userId, new DisplayName(userId.Value), DateTime.UtcNow);
        user.SetAvatar(hash, DateTime.UtcNow);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
