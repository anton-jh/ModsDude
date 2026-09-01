using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
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
    private const int _existenceCheckConcurrency = 16;


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(_imagesContainerName)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> CheckWhichExist(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken)
    {
        var present = new ConcurrentBag<string>();

        // Blob storage has no batch existence API, so the batch is only a batch to the caller. It
        // still saves the round trips that matter — the client's, over the network it is slow on.
        await Parallel.ForEachAsync(
            hashes.Distinct(),
            new ParallelOptions { MaxDegreeOfParallelism = _existenceCheckConcurrency, CancellationToken = cancellationToken },
            async (hash, ct) =>
            {
                if ((await GetBlobClient(hash).ExistsAsync(ct)).Value)
                {
                    present.Add(hash);
                }
            });

        return [.. present];
    }

    public async Task Upload(string hash, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        // Overwriting is safe precisely because the caller verified the bytes hash to the address:
        // whatever is already there is the same file.
        await GetBlobClient(hash).UploadAsync(content, options, cancellationToken);
    }

    public async Task<StoredModImage?> Download(string hash, CancellationToken cancellationToken)
    {
        try
        {
            var result = await GetBlobClient(hash).DownloadStreamingAsync(cancellationToken: cancellationToken);

            return new StoredModImage(result.Value.Content, result.Value.Details.ContentType);
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
            // See ModStorageService.ListStoredMods on the missing timestamp.
            yield return new StoredBlob(blob.Name, blob.Properties.LastModified ?? DateTimeOffset.MaxValue);
        }
    }

    public async Task DeleteStoredBlob(string blobName, CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(_imagesContainerName)
            .GetBlobClient(blobName)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
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
        var validated = ModImageHash.Validated(hash);

        return $"{validated[..2]}/{validated}";
    }
}
