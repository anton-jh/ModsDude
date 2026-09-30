using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Storage.Services;

internal static class StoredBlobDeletion
{
    /// <summary>
    /// Deletes a blob the sweep listed, but only while it is still the blob that was listed. One
    /// written or touched since - an upload that reused it, say - is kept.
    /// </summary>
    /// <returns>Whether it was deleted.</returns>
    public static async Task<bool> DeleteIfUnchangedAsync(BlobContainerClient container, StoredBlob blob, CancellationToken cancellationToken)
    {
        var conditions = blob.Version is string version
            ? new BlobRequestConditions { IfMatch = new ETag(version) }
            : null;

        try
        {
            var response = await container
                .GetBlobClient(blob.Name)
                .DeleteIfExistsAsync(conditions: conditions, cancellationToken: cancellationToken);

            return response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            return false;
        }
    }
}
