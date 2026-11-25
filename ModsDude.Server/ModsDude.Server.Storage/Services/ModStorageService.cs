using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Storage.Services;

internal class ModStorageService(
    BlobServiceClient blobServiceClient,
    IBlobContainers blobContainers,
    IUserDelegationKeyCache keyCache,
    ITimeService timeService)
    : IModStorageService
{
    public string ContentHashMetadataKey => StoredBlob.ContentHashKey;


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await Container().CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<StoredModFile?> GetStoredMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        return ToStoredModFile(await blobContainers.GetAsync(StorageContainer.Mods, BlobNames.Mod(repoId, modId, versionId), cancellationToken));
    }

    public Task<string> GetUploadLink(RepoId repoId, UserId userId, UploadId uploadId, CancellationToken cancellationToken)
    {
        // Put Block needs Write, which could overwrite. The link names the upload's own blob, never an
        // address anything refers to; registering copies it into place without overwriting.
        return BlobSasLinks.CreateAsync(
            keyCache,
            Container().GetBlobClient(BlobNames.Upload(repoId, userId, uploadId)),
            BlobSasPermissions.Create | BlobSasPermissions.Write,
            new DateTimeOffset(timeService.Now()),
            cancellationToken);
    }

    public Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        return BlobSasLinks.CreateAsync(
            keyCache,
            Container().GetBlobClient(BlobNames.Mod(repoId, modId, versionId)),
            BlobSasPermissions.Read,
            new DateTimeOffset(timeService.Now()),
            cancellationToken);
    }

    private static StoredModFile? ToStoredModFile(StoredBlob? blob)
    {
        return blob is null ? null : new StoredModFile(blob.Length, blob.RecordedContentHash);
    }

    private BlobContainerClient Container()
    {
        return blobServiceClient.GetBlobContainerClient(StorageContainerNames.Of(StorageContainer.Mods));
    }
}
