using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Storage;
using System.Collections.Concurrent;

namespace ModsDude.Server.Storage.Services;

/// <summary>
/// Image derivatives, addressed by the SHA-256 of their own bytes. The address carries no repo, so
/// one blob serves every repo that references it — which is what makes deduplication across
/// versions, mods and repos work at all.
/// </summary>
internal class ModImageStorageService(
    BlobServiceClient blobServiceClient,
    IBlobContainers blobContainers)
    : IModImageStorageService
{
    private const int _reuseConcurrency = 16;


    public string ContentType => "image/webp";


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(StorageContainerNames.Of(StorageContainer.Images))
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


    private async Task<bool> TryReuseOne(string hash, CancellationToken cancellationToken)
    {
        return await blobContainers.ClaimAsync(StorageContainer.Images, BlobNames.Image(hash), null, cancellationToken) is not null;
    }

    private BlobClient GetBlobClient(string hash)
    {
        return blobServiceClient
            .GetBlobContainerClient(StorageContainerNames.Of(StorageContainer.Images))
            .GetBlobClient(BlobNames.Image(hash));
    }
}
