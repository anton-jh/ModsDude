using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>Rows that refer to blobs, for the deletion queue and storage maintenance tests.</summary>
internal static class StorageTestData
{
    private static readonly DateTimeOffset _timestamp = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);


    public static async Task<(RepoId RepoId, UserId UserId)> GivenARepoAsync(Func<ApplicationDbContext> createDbContext)
    {
        using var dbContext = createDbContext();

        var userId = new UserId($"user-{Guid.NewGuid()}");
        var user = new User(userId, new DisplayName("Åsa"), DateTime.UtcNow);
        var repo = new Repo(new RepoName($"Gården {Guid.NewGuid()}"), DateTime.UtcNow, user)
        {
            AdapterData = new AdapterData(new AdapterIdentifier("_test@1"), new AdapterConfiguration("{}"))
        };

        dbContext.Users.Add(user);
        dbContext.Repos.Add(repo);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (repo.Id, userId);
    }

    public static async Task GivenAModVersionAsync(
        Func<ApplicationDbContext> createDbContext,
        RepoId repoId,
        string modId,
        string versionId,
        params string[] imageHashes)
    {
        using var dbContext = createDbContext();

        var sequence = await dbContext.ModVersions.CountAsync(x => x.RepoId == repoId && x.ModId == new ModId(modId));

        var version = new ModVersion
        {
            RepoId = repoId,
            ModId = new ModId(modId),
            Id = new ModVersionId(versionId),
            SequenceNumber = sequence,
            DisplayName = versionId,
            Description = "",
            FileName = $"{modId}.zip",
            ContentHash = TestHashes.Of(versionId),
            SizeBytes = 1024,
            Locked = false,
            Attributes = [],
            Created = _timestamp,
            Updated = _timestamp
        };

        version.SetImages(
            [.. imageHashes.Select((hash, position) => new ModImageReference(hash, ModImageKind.StoreImage, ModImageRendition.Full, position, $"{position}.png"))],
            _timestamp);

        dbContext.ModVersions.Add(version);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>A savegame that follows no mod list, with one snapshot per hash.</summary>
    public static async Task<SavegameId> GivenASavegameAsync(
        Func<ApplicationDbContext> createDbContext,
        RepoId repoId,
        UserId userId,
        params string[] contentHashes)
    {
        SavegameId savegameId;

        using (var dbContext = createDbContext())
        {
            var savegame = new Savegame(repoId, new SavegameName($"Höst {Guid.NewGuid()}"), null, DateTime.UtcNow);
            dbContext.Savegames.Add(savegame);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            savegameId = savegame.Id;
        }

        foreach (var contentHash in contentHashes)
        {
            using var dbContext = createDbContext();

            var savegame = (await dbContext.Savegames.GetAsync(repoId, savegameId, CancellationToken.None))!;
            dbContext.SavegameSnapshots.Add(savegame.CreateSnapshot(null, contentHash, 1024, userId, DateTime.UtcNow));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        return savegameId;
    }
}
