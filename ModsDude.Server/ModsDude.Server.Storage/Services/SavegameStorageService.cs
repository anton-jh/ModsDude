using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using System.Runtime.CompilerServices;

namespace ModsDude.Server.Storage.Services;

/// <summary>
/// Packed savegames, in their own container and addressed by the SHA-256 of their own bytes within
/// the savegame that owns them. See <see cref="ISavegameStorageService"/> for why the address is the
/// content here and the identity in <see cref="ModStorageService"/>.
/// </summary>
internal class SavegameStorageService(
    BlobServiceClient blobServiceClient,
    ITimeService timeService)
    : ISavegameStorageService
{
    private const string _savegamesContainerName = "savegames";

    /// <summary>
    /// Sent as <c>x-ms-meta-sha256</c>. The client writes it as it uploads, because the API never
    /// sees the bytes and so cannot compute it; the SAS it uploads over already carries Write, which
    /// is the permission Put Blob needs to set metadata alongside the content.
    /// </summary>
    private const string _contentHashMetadataKey = "sha256";


    public string ContentHashMetadataKey => _contentHashMetadataKey;


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(_savegamesContainerName)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<bool> CheckIfSavegameExists(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        var result = await GetBlobClient(repoId, savegameId, contentHash).ExistsAsync(cancellationToken);
        return result.Value;
    }

    public async Task<long?> GetSavegameSize(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await GetBlobClient(repoId, savegameId, contentHash).GetPropertiesAsync(cancellationToken: cancellationToken);

            return properties.Value.ContentLength;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> TryReuseSavegame(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(repoId, savegameId, contentHash);

        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            // Rewriting the metadata it already has is the cheapest write there is, and any write gives
            // the blob a new ETag - which is what the sweep's delete is conditional on.
            await blobClient.SetMetadataAsync(
                properties.Value.Metadata,
                new BlobRequestConditions { IfMatch = properties.Value.ETag },
                cancellationToken);

            return true;
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            // Gone, or changed under this call: either way the upload goes ahead and writes it again.
            return false;
        }
    }

    public async Task<string?> GetRecordedContentHash(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await GetBlobClient(repoId, savegameId, contentHash).GetPropertiesAsync(cancellationToken: cancellationToken);

            return properties.Value.Metadata.TryGetValue(_contentHashMetadataKey, out var hash) ? hash : null;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public Task<string> GetUploadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        // Write is what lets the client stamp the content hash into blob metadata as it uploads, on
        // top of writing the content itself.
        return GetSasLink(repoId, savegameId, contentHash, BlobSasPermissions.Create | BlobSasPermissions.Write, cancellationToken);
    }

    public Task<string> GetDownloadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        return GetSasLink(repoId, savegameId, contentHash, BlobSasPermissions.Read, cancellationToken);
    }

    public async IAsyncEnumerable<StoredBlob> ListStoredSavegames([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = blobServiceClient.GetBlobContainerClient(_savegamesContainerName);

        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            // See ModStorageService.ListStoredMods on the missing timestamp.
            yield return new StoredBlob(
                blob.Name,
                blob.Properties.LastModified ?? DateTimeOffset.MaxValue,
                blob.Properties.ContentLength ?? 0,
                blob.Properties.ETag?.ToString());
        }
    }

    public Task<bool> DeleteStoredBlob(StoredBlob blob, CancellationToken cancellationToken)
        => StoredBlobDeletion.DeleteIfUnchangedAsync(blobServiceClient.GetBlobContainerClient(_savegamesContainerName), blob, cancellationToken);


    private Task<string> GetSasLink(RepoId repoId, SavegameId savegameId, string contentHash, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(repoId, savegameId, contentHash);

        return BlobSasLinks.CreateAsync(blobServiceClient, blobClient, permissions, new DateTimeOffset(timeService.Now()), cancellationToken);
    }

    private BlobClient GetBlobClient(RepoId repoId, SavegameId savegameId, string contentHash)
    {
        return blobServiceClient
            .GetBlobContainerClient(_savegamesContainerName)
            .GetBlobClient(BuildSavegameFilename(repoId, savegameId, contentHash));
    }

    /// <summary>
    /// The hash is validated rather than taken on trust, because it is a path segment: a caller that
    /// passes something that is not a hash would otherwise mint a link to a name the reclamation
    /// sweep cannot parse, and the sweep never deletes what it cannot parse. Garbage that can never
    /// be collected is the failure worth refusing at the boundary.
    /// </summary>
    private static string BuildSavegameFilename(RepoId repoId, SavegameId savegameId, string contentHash)
    {
        return $"{repoId.Value}/{savegameId.Value}/{ModImageHash.Validated(contentHash)}";
    }
}
