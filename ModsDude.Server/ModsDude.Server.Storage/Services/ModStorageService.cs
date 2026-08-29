using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Storage.Services;
internal class ModStorageService(
    BlobServiceClient blobServiceClient)
    : IModStorageService
{
    private const string _modsContainerName = "mods";
    private const int _sasLifetime = 30;

    /// <summary>
    /// Sent as <c>x-ms-meta-sha256</c>. The client writes it as it uploads, because the API never
    /// sees the bytes and so cannot compute it; the SAS it uploads over already carries Write, which
    /// is the permission Put Blob needs to set metadata alongside the content.
    /// </summary>
    private const string _contentHashMetadataKey = "sha256";


    public string ContentHashMetadataKey => _contentHashMetadataKey;


    public async Task<bool> CheckIfModExists(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        var result = await GetBlobClient(repoId, modId, versionId).ExistsAsync(cancellationToken);
        return result.Value;
    }

    public async Task<string?> GetRecordedContentHash(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await GetBlobClient(repoId, modId, versionId).GetPropertiesAsync(cancellationToken: cancellationToken);

            return properties.Value.Metadata.TryGetValue(_contentHashMetadataKey, out var hash) ? hash : null;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        // Write is what lets the client stamp the content hash into blob metadata as it uploads, on
        // top of writing the content itself.
        return GetSasLink(repoId, modId, versionId, BlobSasPermissions.Create | BlobSasPermissions.Write, cancellationToken);
    }

    public Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        return GetSasLink(repoId, modId, versionId, BlobSasPermissions.Read, cancellationToken);
    }

    public async Task DeleteMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        await GetBlobClient(repoId, modId, versionId).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }


    private async Task<string> GetSasLink(RepoId repoId, ModId modId, ModVersionId versionId, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(repoId, modId, versionId);

        var startsOn = DateTimeOffset.UtcNow;
        var expiresOn = startsOn.AddMinutes(_sasLifetime);

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

    private BlobClient GetBlobClient(RepoId repoId, ModId modId, ModVersionId versionId)
    {
        return blobServiceClient
            .GetBlobContainerClient(_modsContainerName)
            .GetBlobClient(BuildModFilename(repoId, modId, versionId));
    }

    private static string BuildModFilename(RepoId repoId, ModId modId, ModVersionId versionId)
    {
        return $"{repoId.Value}/{modId.Value}/{versionId.Value}";
    }
}
