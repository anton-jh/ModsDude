using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Backups;

namespace ModsDude.Server.Storage.Services;

/// <param name="blobServiceClient">The backup storage account, or <c>null</c> when none is configured.</param>
internal class BackupStorageService(
    BlobServiceClient? blobServiceClient,
    ILogger<BackupStorageService> logger)
    : IBackupStorageService
{
    private const string _containerName = "db-backups";


    public async Task<BackupListing> ListBackups(CancellationToken cancellationToken)
    {
        if (blobServiceClient is null)
        {
            return new BackupListing.NotConfigured();
        }

        var container = blobServiceClient.GetBlobContainerClient(_containerName);
        var blobs = new List<ListedBackupBlob>();

        try
        {
            await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                blobs.Add(new ListedBackupBlob(blob.Name, blob.Properties.ContentLength ?? 0));
            }
        }
        catch (Exception ex) when (ex is RequestFailedException or AuthenticationFailedException)
        {
            logger.LogError(ex, "Could not list the database backups in '{Container}' of '{Account}'.", _containerName, blobServiceClient.AccountName);
            return new BackupListing.Unavailable();
        }

        return new BackupListing.Listed(blobs);
    }
}
