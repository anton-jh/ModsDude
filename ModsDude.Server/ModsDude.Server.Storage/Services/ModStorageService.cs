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


    public async Task<bool> CheckIfModExists(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(_modsContainerName);
        var blobClient = containerClient.GetBlobClient(BuildModFilename(repoId, modId, versionId));

        var result = await blobClient.ExistsAsync(cancellationToken);
        return result.Value;
    }

    public async Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken)
    {
        var blobName = BuildModFilename(repoId, modId, versionId);
        var blobClient = blobServiceClient
            .GetBlobContainerClient(_modsContainerName)
            .GetBlobClient(blobName);

        var userDelegationKey = await blobServiceClient.GetUserDelegationKeyAsync(
            startsOn: DateTimeOffset.UtcNow,
            expiresOn: DateTimeOffset.UtcNow.AddMinutes(_sasLifetime),
            cancellationToken);

        var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Create | BlobSasPermissions.Write, DateTimeOffset.UtcNow.AddMinutes(_sasLifetime))
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_sasLifetime)
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


    private static string BuildModFilename(RepoId repoId, ModId modId, ModVersionId versionId)
    {
        return $"{repoId.Value}/{modId.Value}/{versionId.Value}";
    }
}
