using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Storage;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ModsDude.Server.Storage.Services;

/// <summary>
/// Image derivatives, addressed by the SHA-256 of their own bytes. The address carries no repo, so
/// one blob serves every repo that references it — which is what makes deduplication across
/// versions, mods and repos work at all.
/// </summary>
internal class ModImageStorageService(
    BlobServiceClient blobServiceClient)
    : IModImageStorageService
{
    private const string _imagesContainerName = "mod-images";
    private const int _reuseConcurrency = 16;


    public string ContentType => "image/webp";


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(_imagesContainerName)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> TryReuse(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken)
    {
        var missing = new ConcurrentBag<string>();

        // Blob storage has no batch call, so the batch is only a batch to the caller.
        await Parallel.ForEachAsync(
            hashes.Distinct(),
            new ParallelOptions { MaxDegreeOfParallelism = _reuseConcurrency, CancellationToken = cancellationToken },
            async (hash, ct) =>
            {
                if (!await TryReuseOne(hash, ct))
                {
                    missing.Add(hash);
                }
            });

        return [.. missing.Order(StringComparer.Ordinal)];
    }

    public async Task Upload(string hash, Stream content, CancellationToken cancellationToken)
    {
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = ContentType }
        };

        // Overwriting is safe precisely because the caller verified the bytes hash to the address:
        // whatever is already there is the same file.
        await GetBlobClient(hash).UploadAsync(content, options, cancellationToken);
    }

    public async Task<Stream?> Download(string hash, CancellationToken cancellationToken)
    {
        try
        {
            var result = await GetBlobClient(hash).DownloadStreamingAsync(cancellationToken: cancellationToken);

            return result.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }


    public async IAsyncEnumerable<StoredBlob> ListStoredImages([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = blobServiceClient.GetBlobContainerClient(_imagesContainerName);

        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            if (ContainerBindingMarker.IsMarker(blob.Name))
            {
                continue;
            }

            // See ModStorageService.ListStoredMods on the missing timestamp.
            yield return new StoredBlob(
                blob.Name,
                blob.Properties.LastModified ?? DateTimeOffset.MaxValue,
                blob.Properties.ContentLength ?? 0,
                blob.Properties.ETag?.ToString());
        }
    }

    public Task<bool> DeleteStoredBlob(StoredBlob blob, CancellationToken cancellationToken)
        => StoredBlobDeletion.DeleteIfUnchangedAsync(blobServiceClient.GetBlobContainerClient(_imagesContainerName), blob, cancellationToken);

    public Task<Guid?> ReadBindingMarker(CancellationToken cancellationToken)
        => ContainerBindingMarker.ReadAsync(blobServiceClient.GetBlobContainerClient(_imagesContainerName), cancellationToken);

    public Task WriteBindingMarker(Guid bindingId, CancellationToken cancellationToken)
        => ContainerBindingMarker.WriteAsync(blobServiceClient.GetBlobContainerClient(_imagesContainerName), bindingId, cancellationToken);


    private async Task<bool> TryReuseOne(string hash, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(hash);

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
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            // Written by somebody else between the read and the write, which moved the ETag just the
            // same. The bytes are the same either way: the address is their hash.
            return true;
        }
    }

    private BlobClient GetBlobClient(string hash)
    {
        return blobServiceClient
            .GetBlobContainerClient(_imagesContainerName)
            .GetBlobClient(BuildImageBlobName(hash));
    }

    /// <summary>
    /// The two-character prefix keeps a flat container from becoming one enormous listing partition.
    /// </summary>
    private static string BuildImageBlobName(string hash)
    {
        var validated = Sha256Hex.Validated(hash);

        return $"{validated[..2]}/{validated}";
    }
}
