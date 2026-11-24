using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Storage.Services;

internal class ModStorageService(
    BlobServiceClient blobServiceClient,
    IBlobContainers blobContainers,
    IUserDelegationKeyCache keyCache,
    ITimeService timeService)
    : IModStorageService
{
    /// <summary>
    /// Sent as <c>x-ms-meta-sha256</c>. The client writes it as it commits, because the API never
    /// sees the bytes and so cannot compute it.
    /// </summary>
    private const string _contentHashMetadataKey = "sha256";


    public string ContentHashMetadataKey => _contentHashMetadataKey;


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(StorageContainerNames.Of(StorageContainer.Mods))
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<StoredModFile?> GetStoredMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        return ToStoredModFile(await blobContainers.GetAsync(StorageContainer.Mods, BlobNames.Mod(repoId, modId, versionId), cancellationToken));
    }

    public async Task<StoredModFile?> ClaimStoredMod(RepoId repoId, ModId modId, ModVersionId versionId, ModBlobLabel label, CancellationToken cancellationToken)
    {
        return ToStoredModFile(await blobContainers.ClaimAsync(StorageContainer.Mods, BlobNames.Mod(repoId, modId, versionId), label, cancellationToken));
    }

    public Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        // Put Block needs Write. The link could overwrite an existing blob; the client's commit sends
        // If-None-Match: * so that it never does.
        return GetSasLink(repoId, modId, versionId, BlobSasPermissions.Create | BlobSasPermissions.Write, cancellationToken);
    }

    public Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        return GetSasLink(repoId, modId, versionId, BlobSasPermissions.Read, cancellationToken);
    }

    private static StoredModFile? ToStoredModFile(StoredBlob? blob)
    {
        return blob is null
            ? null
            : new StoredModFile(blob.Length, blob.Metadata.TryGetValue(_contentHashMetadataKey, out var hash) ? hash : null);
    }

    private Task<string> GetSasLink(RepoId repoId, ModId modId, ModVersionId versionId, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(repoId, modId, versionId);

        return BlobSasLinks.CreateAsync(keyCache, blobClient, permissions, new DateTimeOffset(timeService.Now()), cancellationToken);
    }

    private BlobClient GetBlobClient(RepoId repoId, ModId modId, ModVersionId versionId)
    {
        return blobServiceClient
            .GetBlobContainerClient(StorageContainerNames.Of(StorageContainer.Mods))
            .GetBlobClient(BlobNames.Mod(repoId, modId, versionId));
    }
}
