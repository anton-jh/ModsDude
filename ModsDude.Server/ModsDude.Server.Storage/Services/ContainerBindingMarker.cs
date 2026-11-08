using Azure;
using Azure.Storage.Blobs;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Storage.Services;

/// <summary>The blob in each container that names the database it belongs to - see <see cref="StorageBinding"/>.</summary>
internal static class ContainerBindingMarker
{
    public static bool IsMarker(string blobName) => blobName == StorageBinding.MarkerBlobName;

    /// <returns>The id the marker names, or null where there is no marker.</returns>
    public static async Task<Guid?> ReadAsync(BlobContainerClient container, CancellationToken cancellationToken)
    {
        try
        {
            var content = await container.GetBlobClient(StorageBinding.MarkerBlobName).DownloadContentAsync(cancellationToken);
            var text = content.Value.Content.ToString();

            return Guid.TryParse(text, out var bindingId)
                ? bindingId
                : throw new InvalidOperationException($"The storage binding marker in container '{container.Name}' does not hold an id.");
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Writes the marker where there is none. A marker that is already there is never replaced.</summary>
    public static async Task WriteAsync(BlobContainerClient container, Guid bindingId, CancellationToken cancellationToken)
    {
        try
        {
            await container.GetBlobClient(StorageBinding.MarkerBlobName).UploadAsync(BinaryData.FromString(bindingId.ToString()), overwrite: false, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            if (await ReadAsync(container, cancellationToken) != bindingId)
            {
                throw new InvalidOperationException($"Container '{container.Name}' was bound to another database while this one was binding it.", exception);
            }
        }
    }
}
