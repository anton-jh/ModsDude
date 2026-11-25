using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Storage;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The database queues a blob whenever a row stops referring to it, by any statement and through any
/// cascade. A path the triggers miss leaves an orphan the audit reports; these hold them to every path
/// that exists, and hold the names they build to <see cref="BlobNames"/>.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class BlobDeletionQueueTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Deleting_a_mod_version_queues_its_blob()
    {
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1.0.0");

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.ModVersions.Where(x => x.RepoId == repoId).ExecuteDeleteAsync();
        }

        var candidate = Assert.Single(await CandidatesAsync(BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1.0.0"))));
        Assert.Equal((StorageContainer.Mods, BlobDeletionReason.ReferenceRemoved, repoId), (candidate.Container, candidate.Reason, candidate.RepoId!.Value));
    }

    [Fact]
    public async Task Deleting_a_repo_queues_every_blob_in_it()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var image = TestHashes.Of($"image-{Guid.NewGuid()}");
        var saveHash = TestHashes.Of($"save-{Guid.NewGuid()}");
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1.0.0", image);
        var savegameId = await StorageTestData.GivenASavegameAsync(fixture.CreateDbContext, repoId, userId, saveHash);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.DeleteWithContentsAsync((await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!, CancellationToken.None);
        }

        Assert.Single(await CandidatesAsync(BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1.0.0"))));
        Assert.Single(await CandidatesAsync(BlobNames.Savegame(repoId, savegameId, saveHash)));
        Assert.Single(await CandidatesAsync(BlobNames.Image(image)));
    }

    [Fact]
    public async Task Deleting_a_savegame_queues_a_shared_blob_once()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var shared = TestHashes.Of($"save-{Guid.NewGuid()}");
        var other = TestHashes.Of($"save-{Guid.NewGuid()}");
        var savegameId = await StorageTestData.GivenASavegameAsync(fixture.CreateDbContext, repoId, userId, shared, other, shared);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Savegames.Where(x => x.Id == savegameId).ExecuteDeleteAsync();
        }

        var candidate = Assert.Single(await CandidatesAsync(BlobNames.Savegame(repoId, savegameId, shared)));
        Assert.Equal(1, candidate.Requeued);
        Assert.Single(await CandidatesAsync(BlobNames.Savegame(repoId, savegameId, other)));
    }

    [Fact]
    public async Task Pruning_snapshots_queues_their_blobs()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var pruned = TestHashes.Of($"save-{Guid.NewGuid()}");
        var savegameId = await StorageTestData.GivenASavegameAsync(fixture.CreateDbContext, repoId, userId, pruned, TestHashes.Of($"save-{Guid.NewGuid()}"));

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.SavegameSnapshots.DeleteSnapshotsAsync(repoId, savegameId, [new Domain.Savegames.SavegameSnapshotNumber(1)], CancellationToken.None);
        }

        Assert.Single(await CandidatesAsync(BlobNames.Savegame(repoId, savegameId, pruned)));
    }

    [Fact]
    public async Task Replacing_a_versions_images_queues_the_ones_it_had()
    {
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var dropped = TestHashes.Of($"image-{Guid.NewGuid()}");
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1.0.0", dropped);

        using (var dbContext = fixture.CreateDbContext())
        {
            var version = await dbContext.ModVersions.SingleAsync(x => x.RepoId == repoId);
            version.SetImages([], DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        Assert.Single(await CandidatesAsync(BlobNames.Image(dropped)));
    }

    [Fact]
    public async Task Changing_or_removing_a_picture_queues_the_one_it_replaced()
    {
        var first = TestHashes.Of($"avatar-{Guid.NewGuid()}");
        var second = TestHashes.Of($"avatar-{Guid.NewGuid()}");
        var userId = new UserId($"user-{Guid.NewGuid()}");

        using (var dbContext = fixture.CreateDbContext())
        {
            var user = new User(userId, new DisplayName("Anton"), DateTime.UtcNow);
            user.SetAvatar(first, DateTime.UtcNow);
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        await SetAvatarAsync(userId, second);
        Assert.Empty(await CandidatesAsync(BlobNames.Image(second)));
        Assert.Equal(userId, Assert.Single(await CandidatesAsync(BlobNames.Image(first))).UserId);

        await SetAvatarAsync(userId, null);
        Assert.Single(await CandidatesAsync(BlobNames.Image(second)));
    }

    [Fact]
    public async Task Deleting_a_user_queues_their_picture()
    {
        var avatar = TestHashes.Of($"avatar-{Guid.NewGuid()}");
        var userId = new UserId($"user-{Guid.NewGuid()}");

        using (var dbContext = fixture.CreateDbContext())
        {
            var user = new User(userId, new DisplayName("Anton"), DateTime.UtcNow);
            user.SetAvatar(avatar, DateTime.UtcNow);
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
        }

        var candidate = Assert.Single(await CandidatesAsync(BlobNames.Image(avatar)));
        Assert.Equal((StorageContainer.Images, BlobDeletionReason.ReferenceRemoved, userId), (candidate.Container, candidate.Reason, candidate.UserId!.Value));
    }

    [Fact]
    public async Task Renaming_a_mod_version_queues_the_blob_it_named_and_editing_it_otherwise_does_not()
    {
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1.0.0");

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlAsync($"""UPDATE "ModVersions" SET "DisplayName" = 'Renamed' WHERE "RepoId" = {repoId.Value}""");
            Assert.Empty(await CandidatesAsync(BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1.0.0"))));

            await dbContext.Database.ExecuteSqlAsync($"""UPDATE "ModVersions" SET "Id" = '2.0.0' WHERE "RepoId" = {repoId.Value}""");
        }

        Assert.Single(await CandidatesAsync(BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId("1.0.0"))));
    }

    [Fact]
    public async Task Changing_a_snapshots_content_hash_queues_the_blob_it_named()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var before = TestHashes.Of($"save-{Guid.NewGuid()}");
        var after = TestHashes.Of($"save-{Guid.NewGuid()}");
        var savegameId = await StorageTestData.GivenASavegameAsync(fixture.CreateDbContext, repoId, userId, before);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlAsync($"""UPDATE "SavegameSnapshots" SET "ContentHash" = {after} WHERE "SavegameId" = {savegameId.Value}""");
        }

        Assert.Single(await CandidatesAsync(BlobNames.Savegame(repoId, savegameId, before)));
        Assert.Empty(await CandidatesAsync(BlobNames.Savegame(repoId, savegameId, after)));
    }

    [Fact]
    public async Task Changing_an_image_references_hash_queues_the_image_it_named()
    {
        var (repoId, _) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var before = TestHashes.Of($"image-{Guid.NewGuid()}");
        var after = TestHashes.Of($"image-{Guid.NewGuid()}");
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "a_mod", "1.0.0", before);

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlAsync($"""UPDATE "ModImageReference" SET "Hash" = {after} WHERE "Hash" = {before}""");
        }

        Assert.Single(await CandidatesAsync(BlobNames.Image(before)));
    }

    [Fact]
    public async Task Queuing_a_blob_again_moves_when_it_was_queued_and_counts_the_requeue()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var name = BlobNames.Mod(repoId, new ModId("a_mod"), new ModVersionId(Guid.NewGuid().ToString()));

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.QueueAdminAsync(StorageContainer.Mods, name, "v1", "admin", CancellationToken.None);
        }

        var first = Assert.Single(await CandidatesAsync(name));

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.QueueUploadAsync(StorageContainer.Mods, name, repoId, userId, CancellationToken.None);
        }

        var candidate = Assert.Single(await CandidatesAsync(name));
        Assert.Equal((BlobDeletionReason.UploadIssued, 1, userId), (candidate.Reason, candidate.Requeued, candidate.UserId!.Value));
        Assert.True(candidate.QueuedAt > first.QueuedAt);
        Assert.Null(candidate.ExpectedVersion);
    }

    [Fact]
    public async Task An_admin_choice_is_queued_with_the_version_it_was_chosen_at()
    {
        var name = BlobNames.Image(TestHashes.Of($"image-{Guid.NewGuid()}"));

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.QueueAdminAsync(StorageContainer.Images, name, "v7", "anton", CancellationToken.None);
        }

        var candidate = Assert.Single(await CandidatesAsync(name));
        Assert.Equal((BlobDeletionReason.Admin, "v7", "anton"), (candidate.Reason, candidate.ExpectedVersion, candidate.RequestedBy));
    }

    [Fact]
    public async Task Every_name_a_trigger_builds_is_one_the_deletion_run_recognises()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        await StorageTestData.GivenAModVersionAsync(fixture.CreateDbContext, repoId, "FS25_Mod", "1.0.0.0", TestHashes.Of($"image-{Guid.NewGuid()}"));
        await StorageTestData.GivenASavegameAsync(fixture.CreateDbContext, repoId, userId, TestHashes.Of($"save-{Guid.NewGuid()}"));

        using (var dbContext = fixture.CreateDbContext())
        {
            await dbContext.DeleteWithContentsAsync((await dbContext.Repos.GetAsync(repoId, CancellationToken.None))!, CancellationToken.None);
        }

        using var verification = fixture.CreateDbContext();
        var queued = await verification.BlobDeletionCandidates.Where(x => x.RepoId == repoId).ToListAsync();

        Assert.Equal(3, queued.Count);
        Assert.All(queued, x => Assert.True(BlobNames.IsRecognised(x.Container, x.BlobName), x.BlobName));
    }


    private async Task<List<BlobDeletionCandidate>> CandidatesAsync(string blobName)
    {
        using var dbContext = fixture.CreateDbContext();

        return await dbContext.BlobDeletionCandidates.AsNoTracking().Where(x => x.BlobName == blobName).ToListAsync();
    }

    private async Task SetAvatarAsync(UserId userId, string? hash)
    {
        using var dbContext = fixture.CreateDbContext();

        var user = await dbContext.Users.SingleAsync(x => x.Id == userId);
        user.SetAvatar(hash, DateTime.UtcNow);
        await dbContext.SaveChangesAsync();
    }
}
