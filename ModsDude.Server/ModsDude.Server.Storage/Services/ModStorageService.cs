using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using System.Runtime.CompilerServices;

namespace ModsDude.Server.Storage.Services;
internal class ModStorageService(
    BlobServiceClient blobServiceClient,
    IUserDelegationKeyCache keyCache,
    ITimeService timeService)
    : IModStorageService
{
    private const string _modsContainerName = "mods";

    /// <summary>
    /// Sent as <c>x-ms-meta-sha256</c>. The client writes it as it commits, because the API never
    /// sees the bytes and so cannot compute it.
    /// </summary>
    private const string _contentHashMetadataKey = "sha256";

    private const int _claimAttempts = 3;


    public string ContentHashMetadataKey => _contentHashMetadataKey;


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(_modsContainerName)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<StoredModFile?> GetStoredMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await GetBlobClient(repoId, modId, versionId).GetPropertiesAsync(cancellationToken: cancellationToken);

            return ToStoredModFile(properties.Value);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<StoredModFile?> ClaimStoredMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(repoId, modId, versionId);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

                // Rewriting the metadata it already has is the cheapest write there is, and any write gives
                // the blob a new ETag - which is what the sweep's delete is conditional on.
                await blobClient.SetMetadataAsync(
                    properties.Value.Metadata,
                    new BlobRequestConditions { IfMatch = properties.Value.ETag },
                    cancellationToken);

                return ToStoredModFile(properties.Value);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return null;
            }
            catch (RequestFailedException exception) when (exception.Status == 412 && attempt < _claimAttempts)
            {
                // Another claim of the same blob wrote between the read and the write. The bytes cannot
                // have changed, because no link can overwrite them, so reading again is enough.
            }
        }
    }

    public Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        // Create without Write: a new blob can be written, metadata and all, but one that exists cannot
        // be overwritten - by a second uploader of the same file, or by this link after the file has been
        // registered.
        return GetSasLink(repoId, modId, versionId, BlobSasPermissions.Create, cancellationToken);
    }

    public Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        return GetSasLink(repoId, modId, versionId, BlobSasPermissions.Read, cancellationToken);
    }

    public async Task DeleteMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        await GetBlobClient(repoId, modId, versionId).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<StoredBlob> ListStoredMods([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = blobServiceClient.GetBlobContainerClient(_modsContainerName);

        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            if (ContainerBindingMarker.IsMarker(blob.Name))
            {
                continue;
            }

            // A blob storage does not date is treated as written this instant, so the sweep's grace
            // period retains it. Erring the other way would delete on missing information.
            yield return new StoredBlob(
                blob.Name,
                blob.Properties.LastModified ?? DateTimeOffset.MaxValue,
                blob.Properties.ContentLength ?? 0,
                blob.Properties.ETag?.ToString());
        }
    }

    public Task<bool> DeleteStoredBlob(StoredBlob blob, CancellationToken cancellationToken)
        => StoredBlobDeletion.DeleteIfUnchangedAsync(blobServiceClient.GetBlobContainerClient(_modsContainerName), blob, cancellationToken);

    public Task<Guid?> ReadBindingMarker(CancellationToken cancellationToken)
        => ContainerBindingMarker.ReadAsync(blobServiceClient.GetBlobContainerClient(_modsContainerName), cancellationToken);

    public Task WriteBindingMarker(Guid bindingId, CancellationToken cancellationToken)
        => ContainerBindingMarker.WriteAsync(blobServiceClient.GetBlobContainerClient(_modsContainerName), bindingId, cancellationToken);


    private static StoredModFile ToStoredModFile(BlobProperties properties)
    {
        return new StoredModFile(
            properties.ContentLength,
            properties.Metadata.TryGetValue(_contentHashMetadataKey, out var hash) ? hash : null);
    }

    private Task<string> GetSasLink(RepoId repoId, ModId modId, ModVersionId versionId, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(repoId, modId, versionId);

        return BlobSasLinks.CreateAsync(keyCache, blobClient, permissions, new DateTimeOffset(timeService.Now()), cancellationToken);
    }

    private BlobClient GetBlobClient(RepoId repoId, ModId modId, ModVersionId versionId)
    {
        return blobServiceClient
            .GetBlobContainerClient(_modsContainerName)
            .GetBlobClient(BuildModFilename(repoId, modId, versionId));
    }

    /// <summary>
    /// The segments are checked again here, although the id types already check them, because this is
    /// where a bad one would turn into another repo's blob.
    /// </summary>
    private static string BuildModFilename(RepoId repoId, ModId modId, ModVersionId versionId)
    {
        return $"{repoId.Value}/{BlobPathSegment.Validated(modId.Value)}/{BlobPathSegment.Validated(versionId.Value)}";
    }
}
