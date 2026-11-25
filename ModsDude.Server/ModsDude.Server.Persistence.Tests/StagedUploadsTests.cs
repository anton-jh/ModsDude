using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.Storage;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// A file reaches its address only by being claimed there or copied there from its uploader's own
/// upload, and a copy never replaces what is stored. What must hold: nothing is copied that disagrees with
/// what the request says, the address is queued before anything is copied to it, and a lost race or a
/// changed upload is answered rather than written over.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class StagedUploadsTests(DatabaseFixture fixture)
{
    private static readonly string _hash = TestHashes.Of("the file");
    private static readonly string _otherHash = TestHashes.Of("another file");
    private static readonly ModId _modId = new("a_mod");
    private static readonly ModVersionId _versionId = new("1.0.0");

    private readonly FakeBlobContainers _blobs = new();


    [Fact]
    public async Task A_mod_upload_is_copied_to_its_address_with_its_hash_and_label()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var uploadId = GivenAnUpload(StorageContainer.Mods, repoId, userId, _hash, length: 42);

        var result = await StoreModAsync(repoId, userId, uploadId, _hash);

        Assert.Equal(StagedUploadOutcome.Stored, result.Outcome);
        var stored = _blobs.Find(StorageContainer.Mods, BlobNames.Mod(repoId, _modId, _versionId))!;
        Assert.Equal((42L, _hash), (stored.Length, stored.RecordedContentHash));
        Assert.True(BlobLabel.IsPresentIn(stored.Metadata));
        Assert.Equal(stored, result.Blob);
    }

    [Fact]
    public async Task The_address_is_queued_before_anything_is_copied_to_it()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var uploadId = GivenAnUpload(StorageContainer.Mods, repoId, userId, _hash);
        var address = BlobNames.Mod(repoId, _modId, _versionId);

        var queuedWhenCopied = false;
        _blobs.BeforePromote = (_, _) =>
        {
            using var dbContext = fixture.CreateDbContext();
            queuedWhenCopied = dbContext.BlobDeletionCandidates.Any(x => x.BlobName == address);
        };
        await StoreModAsync(repoId, userId, uploadId, _hash);

        Assert.True(queuedWhenCopied);
    }

    [Fact]
    public async Task Only_the_recorded_hash_is_carried_over_from_the_upload()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var uploadId = GivenAnUpload(StorageContainer.Mods, repoId, userId, _hash, extraMetadata: new() { ["filename"] = "not what it is", ["anything"] = "else" });

        await StoreModAsync(repoId, userId, uploadId, _hash);

        var stored = _blobs.Find(StorageContainer.Mods, BlobNames.Mod(repoId, _modId, _versionId))!;
        Assert.DoesNotContain("anything", stored.Metadata.Keys);
        Assert.Equal("a_mod.zip", BlobMetadataText.Decode(stored.Metadata[BlobLabel.FileNameKey]));
    }

    [Fact]
    public async Task A_file_already_at_the_address_is_claimed_and_nothing_is_copied()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var address = BlobNames.Mod(repoId, _modId, _versionId);
        var before = _blobs.Put(StorageContainer.Mods, address, length: 7, metadata: HashMetadata(_hash));

        var result = await StoreModAsync(repoId, userId, null, _hash);

        Assert.Equal(StagedUploadOutcome.Stored, result.Outcome);
        Assert.Equal(7, result.Blob!.Length);
        Assert.NotEqual(before.Version, _blobs.Find(StorageContainer.Mods, address)!.Version);
    }

    [Fact]
    public async Task A_file_at_the_address_recording_another_hash_is_refused_and_left_alone()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        _blobs.Put(StorageContainer.Mods, BlobNames.Mod(repoId, _modId, _versionId), metadata: HashMetadata(_otherHash));
        var uploadId = GivenAnUpload(StorageContainer.Mods, repoId, userId, _hash);

        var result = await StoreModAsync(repoId, userId, uploadId, _hash);

        Assert.Equal(StagedUploadOutcome.HashMismatch, result.Outcome);
        Assert.Equal(_otherHash, result.Blob!.RecordedContentHash);
        Assert.Equal(_otherHash, _blobs.Find(StorageContainer.Mods, BlobNames.Mod(repoId, _modId, _versionId))!.RecordedContentHash);
    }

    [Fact]
    public async Task An_upload_recording_another_hash_is_refused_before_anything_is_queued_or_copied()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var uploadId = GivenAnUpload(StorageContainer.Mods, repoId, userId, _otherHash);
        var address = BlobNames.Mod(repoId, _modId, _versionId);

        var result = await StoreModAsync(repoId, userId, uploadId, _hash);

        Assert.Equal(StagedUploadOutcome.HashMismatch, result.Outcome);
        Assert.Null(_blobs.Find(StorageContainer.Mods, address));

        using var dbContext = fixture.CreateDbContext();
        Assert.False(await dbContext.BlobDeletionCandidates.AnyAsync(x => x.BlobName == address));
    }

    [Fact]
    public async Task Nothing_at_the_address_and_no_upload_is_missing()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);

        Assert.Equal(StagedUploadOutcome.Missing, (await StoreModAsync(repoId, userId, null, _hash)).Outcome);
        Assert.Equal(StagedUploadOutcome.Missing, (await StoreModAsync(repoId, userId, new UploadId(Guid.NewGuid()), _hash)).Outcome);
    }

    [Fact]
    public async Task Somebody_elses_upload_is_never_copied()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var uploadId = GivenAnUpload(StorageContainer.Mods, repoId, new UserId("somebody-else"), _hash);

        var result = await StoreModAsync(repoId, userId, uploadId, _hash);

        Assert.Equal(StagedUploadOutcome.Missing, result.Outcome);
        Assert.Null(_blobs.Find(StorageContainer.Mods, BlobNames.Mod(repoId, _modId, _versionId)));
    }

    [Fact]
    public async Task An_upload_written_to_while_it_is_copied_is_answered_as_changed_and_nothing_is_stored()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var uploadId = GivenAnUpload(StorageContainer.Mods, repoId, userId, _hash);

        _blobs.BeforePromote = (container, _) => _blobs.Touch(container, BlobNames.Upload(repoId, userId, uploadId));
        var result = await StoreModAsync(repoId, userId, uploadId, _hash);

        Assert.Equal(StagedUploadOutcome.UploadChanged, result.Outcome);
        Assert.Null(_blobs.Find(StorageContainer.Mods, BlobNames.Mod(repoId, _modId, _versionId)));
    }

    [Fact]
    public async Task A_savegame_copied_to_its_address_first_by_somebody_else_is_claimed_rather_than_written_over()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var savegameId = new SavegameId(Guid.NewGuid());
        var address = BlobNames.Savegame(repoId, savegameId, _hash);
        var uploadId = GivenAnUpload(StorageContainer.Savegames, repoId, userId, _otherHash, length: 10);

        _blobs.BeforePromote = (container, destination) => _blobs.Put(container, destination, length: 12, metadata: HashMetadata(_otherHash));
        var result = await StoreSavegameAsync(repoId, savegameId, userId, uploadId, sizeBytes: 10);

        Assert.Equal(StagedUploadOutcome.Stored, result.Outcome);
        Assert.Equal(12, result.Blob!.Length);
        Assert.Equal(12, _blobs.Find(StorageContainer.Savegames, address)!.Length);
    }

    [Fact]
    public async Task A_savegame_upload_of_another_length_than_described_is_refused()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var savegameId = new SavegameId(Guid.NewGuid());
        var uploadId = GivenAnUpload(StorageContainer.Savegames, repoId, userId, _otherHash, length: 10);

        var result = await StoreSavegameAsync(repoId, savegameId, userId, uploadId, sizeBytes: 11);

        Assert.Equal(StagedUploadOutcome.UploadInvalid, result.Outcome);
        Assert.Null(_blobs.Find(StorageContainer.Savegames, BlobNames.Savegame(repoId, savegameId, _hash)));
    }

    [Fact]
    public async Task A_savegame_upload_recording_no_hash_is_refused()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var savegameId = new SavegameId(Guid.NewGuid());
        var uploadId = new UploadId(Guid.NewGuid());
        _blobs.Put(StorageContainer.Savegames, BlobNames.Upload(repoId, userId, uploadId), length: 10);

        var result = await StoreSavegameAsync(repoId, savegameId, userId, uploadId, sizeBytes: 10);

        Assert.Equal(StagedUploadOutcome.UploadInvalid, result.Outcome);
    }

    [Fact]
    public async Task A_savegame_already_stored_is_taken_at_its_stored_length_whatever_the_request_says()
    {
        var (repoId, userId) = await StorageTestData.GivenARepoAsync(fixture.CreateDbContext);
        var savegameId = new SavegameId(Guid.NewGuid());
        _blobs.Put(StorageContainer.Savegames, BlobNames.Savegame(repoId, savegameId, _hash), length: 99, metadata: HashMetadata(_otherHash));

        var result = await StoreSavegameAsync(repoId, savegameId, userId, null, sizeBytes: 5);

        Assert.Equal(StagedUploadOutcome.Stored, result.Outcome);
        Assert.Equal(99, result.Blob!.Length);
    }


    private UploadId GivenAnUpload(StorageContainer container, RepoId repoId, UserId userId, string recordedHash, long length = 1, Dictionary<string, string>? extraMetadata = null)
    {
        var uploadId = new UploadId(Guid.NewGuid());
        var metadata = HashMetadata(recordedHash);

        foreach (var (key, value) in extraMetadata ?? [])
        {
            metadata[key] = value;
        }

        _blobs.Put(container, BlobNames.Upload(repoId, userId, uploadId), length, metadata: metadata);

        return uploadId;
    }

    private static Dictionary<string, string> HashMetadata(string hash) => new() { [StoredBlob.ContentHashKey] = hash };

    private async Task<StagedUploadResult> StoreModAsync(RepoId repoId, UserId userId, UploadId? uploadId, string contentHash)
    {
        using var dbContext = fixture.CreateDbContext();

        var label = new ModBlobLabel(repoId, "A repo", new AdapterIdentifier("_test@1"), userId, "Anton", DateTime.UtcNow, _modId, _versionId, "A mod", "a_mod.zip");

        return await StagedUploads(dbContext).StoreModAsync(repoId, _modId, _versionId, userId, uploadId, contentHash, label, CancellationToken.None);
    }

    private async Task<StagedUploadResult> StoreSavegameAsync(RepoId repoId, SavegameId savegameId, UserId userId, UploadId? uploadId, long sizeBytes)
    {
        using var dbContext = fixture.CreateDbContext();

        var label = new SavegameBlobLabel(repoId, "A repo", new AdapterIdentifier("_test@1"), userId, "Anton", DateTime.UtcNow, savegameId, "A save");

        return await StagedUploads(dbContext).StoreSavegameAsync(repoId, savegameId, _hash, userId, uploadId, sizeBytes, label, CancellationToken.None);
    }

    private StagedUploads StagedUploads(DbContexts.ApplicationDbContext dbContext) => new(dbContext, _blobs, NullLogger<StagedUploads>.Instance);
}
