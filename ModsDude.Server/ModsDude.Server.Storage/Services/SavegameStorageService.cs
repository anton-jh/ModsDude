using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Storage.Services;

/// <summary>
/// Packed savegames, in their own container and addressed by the SHA-256 of their own bytes within
/// the savegame that owns them. See <see cref="ISavegameStorageService"/> for why the address is the
/// content here and the identity in <see cref="ModStorageService"/>.
/// </summary>
internal class SavegameStorageService(
    BlobServiceClient blobServiceClient,
    IBlobContainers blobContainers,
    IUserDelegationKeyCache keyCache,
    ITimeService timeService)
    : ISavegameStorageService
{
    public string ContentHashMetadataKey => StoredBlob.ContentHashKey;


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await Container().CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public Task<string> GetUploadLink(RepoId repoId, UserId userId, UploadId uploadId, CancellationToken cancellationToken)
    {
        // Put Block needs Write, which could overwrite. The link names the upload's own blob, never an
        // address anything refers to; checking in copies it into place without overwriting.
        return BlobSasLinks.CreateAsync(
            keyCache,
            Container().GetBlobClient(BlobNames.Upload(repoId, userId, uploadId)),
            BlobSasPermissions.Create | BlobSasPermissions.Write,
            new DateTimeOffset(timeService.Now()),
            cancellationToken);
    }

    public Task<string> GetDownloadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        return BlobSasLinks.CreateAsync(
            keyCache,
            Container().GetBlobClient(BlobNames.Savegame(repoId, savegameId, contentHash)),
            BlobSasPermissions.Read,
            new DateTimeOffset(timeService.Now()),
            cancellationToken);
    }

    public async Task<bool> ClaimSavegame(RepoId repoId, SavegameId savegameId, string contentHash, SavegameBlobLabel? label, CancellationToken cancellationToken)
    {
        return await blobContainers.ClaimAsync(StorageContainer.Savegames, BlobNames.Savegame(repoId, savegameId, contentHash), label, cancellationToken) is not null;
    }


    private BlobContainerClient Container()
    {
        return blobServiceClient.GetBlobContainerClient(StorageContainerNames.Of(StorageContainer.Savegames));
    }
}
