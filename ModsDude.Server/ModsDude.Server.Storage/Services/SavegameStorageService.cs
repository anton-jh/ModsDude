using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using ModsDude.Server.Application.Dependencies;
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
    BlobServiceClient blobServiceClient)
    : ISavegameStorageService
{
    private const string _savegamesContainerName = "savegames";
    private const int _sasLifetime = 30;

    /// <summary>
    /// How far a SAS is backdated to absorb the difference between this server's clock and the
    /// storage account's. Generous on purpose: the cost of too much is a credential usable slightly
    /// earlier than intended, and the cost of too little is an upload that fails outright.
    /// </summary>
    private static readonly TimeSpan _clockSkewAllowance = TimeSpan.FromMinutes(5);

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

    public async Task DeleteSavegame(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        await GetBlobClient(repoId, savegameId, contentHash).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<StoredBlob> ListStoredSavegames([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = blobServiceClient.GetBlobContainerClient(_savegamesContainerName);

        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            // See ModStorageService.ListStoredMods on the missing timestamp.
            yield return new StoredBlob(blob.Name, blob.Properties.LastModified ?? DateTimeOffset.MaxValue);
        }
    }

    public async Task DeleteStoredBlob(string blobName, CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(_savegamesContainerName)
            .GetBlobClient(blobName)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }


    private async Task<string> GetSasLink(RepoId repoId, SavegameId savegameId, string contentHash, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(repoId, savegameId, contentHash);

        // Backdated, because the signature is checked against Azure's clock rather than ours. A key
        // starting at this instant is rejected outright by a storage node running a second behind -
        // "Signature not valid in the specified time frame" - and the failure lands on the client
        // mid-upload, where it reads as an authentication problem rather than as the clock difference
        // it is. The window still ends _sasLifetime from now, so nothing is valid for longer.
        var startsOn = DateTimeOffset.UtcNow - _clockSkewAllowance;
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(_sasLifetime);

        var userDelegationKey = await blobServiceClient.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken);

        var sasBuilder = new BlobSasBuilder(permissions, expiresOn)
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = startsOn,
            ExpiresOn = expiresOn
        };

        var uriBuilder = new BlobUriBuilder(blobClient.Uri)
        {
            Sas = sasBuilder.ToSasQueryParameters(
                userDelegationKey,
                blobClient
                    .GetParentBlobContainerClient()
                    .GetParentBlobServiceClient()
                    .AccountName)
        };

        return uriBuilder.ToUri().ToString();
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
