using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Storage;
using System.Runtime.CompilerServices;
using System.Text;

namespace ModsDude.Server.Storage.Services;

internal class BlobContainers(
    BlobServiceClient blobServiceClient,
    IUserDelegationKeyCache keyCache,
    ITimeService timeService,
    ILogger<BlobContainers> logger)
    : IBlobContainers
{
    private const int _claimAttempts = 3;

    /// <summary>
    /// How much of an upload one Put Block From URL copies. Storage copies inside the account, so a block
    /// this size takes a moment, and a 2 GB mod is twenty of them.
    /// </summary>
    private const long _promotionBlockSize = 100L * 1024 * 1024;

    private const int _promotionBlocksAtOnce = 4;


    public string StorageAccount => blobServiceClient.Uri.Host;


    public async IAsyncEnumerable<StoredBlob> ListAsync(StorageContainer container, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in Client(container).GetBlobsAsync(traits: BlobTraits.Metadata, cancellationToken: cancellationToken))
        {
            if (item.Name == StorageBinding.MarkerBlobName)
            {
                continue;
            }

            // A blob storage does not date is treated as written this instant, so nothing judges it old
            // enough to delete. Erring the other way would delete on missing information.
            yield return new StoredBlob(
                item.Name,
                item.Properties.LastModified ?? DateTimeOffset.MaxValue,
                item.Properties.ContentLength ?? 0,
                item.Properties.ETag?.ToString(),
                Metadata(item.Metadata));
        }
    }

    public async Task<bool> IsEmptyAsync(StorageContainer container, CancellationToken cancellationToken)
    {
        await foreach (var item in Client(container).GetBlobsAsync(cancellationToken: cancellationToken))
        {
            if (item.Name != StorageBinding.MarkerBlobName)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<StoredBlob?> GetAsync(StorageContainer container, string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var properties = (await Client(container).GetBlobClient(blobName).GetPropertiesAsync(cancellationToken: cancellationToken)).Value;

            return new StoredBlob(blobName, properties.LastModified, properties.ContentLength, properties.ETag.ToString(), Metadata(properties.Metadata));
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<StoredBlob?> ClaimAsync(StorageContainer container, string blobName, BlobLabel? label, CancellationToken cancellationToken)
    {
        var blobClient = Client(container).GetBlobClient(blobName);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var properties = (await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
                var metadata = Metadata(properties.Metadata);
                var conditions = new BlobRequestConditions { IfMatch = properties.ETag };

                try
                {
                    await blobClient.SetMetadataAsync(new Dictionary<string, string>(label?.ApplyTo(metadata) ?? metadata), conditions, cancellationToken);
                }
                catch (RequestFailedException exception) when (exception.Status == 400 && label is not null)
                {
                    // The label is for people; the write is what claims the blob.
                    logger.LogWarning(exception, "Storage refused the label for {Name} in container {Container}. Claiming it without one.", blobName, StorageContainerNames.Of(container));
                    await blobClient.SetMetadataAsync(new Dictionary<string, string>(metadata), conditions, cancellationToken);
                }

                return new StoredBlob(blobName, properties.LastModified, properties.ContentLength, properties.ETag.ToString(), metadata);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return null;
            }
            catch (RequestFailedException exception) when (exception.Status == 412 && attempt < _claimAttempts)
            {
                // Something wrote to the blob, or deleted it, between the read and the write. Only this
                // call's own write proves the blob is there, so it reads again.
            }
            catch (RequestFailedException exception) when (exception.Status == 412)
            {
                throw new BlobBusyException(container, blobName, exception);
            }
        }
    }

    public async Task<BlobPromotion> PromoteAsync(
        StorageContainer container, StoredBlob upload, string destination,
        IReadOnlyDictionary<string, string> metadata, BlobLabel label, CancellationToken cancellationToken)
    {
        if (upload.Version is not string version)
        {
            return new BlobPromotion(BlobPromotionResult.UploadChanged, null);
        }

        var source = Client(container).GetBlobClient(upload.Name);
        var target = Client(container).GetBlockBlobClient(destination);
        var sourceLink = new Uri(await BlobSasLinks.CreateAsync(keyCache, source, BlobSasPermissions.Read, new DateTimeOffset(timeService.Now()), cancellationToken));

        // Ids of their own for every copy, so two copies to one destination at once never commit each
        // other's blocks. Every id has the same length, as storage requires.
        var copyId = Guid.NewGuid();
        var blockCount = (int)((upload.Length + _promotionBlockSize - 1) / _promotionBlockSize);
        var blockIds = Enumerable.Range(0, blockCount)
            .Select(x => Convert.ToBase64String(Encoding.ASCII.GetBytes($"{copyId:N}{x:D6}")))
            .ToList();

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, blockCount),
                new ParallelOptions { MaxDegreeOfParallelism = _promotionBlocksAtOnce, CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    var offset = index * _promotionBlockSize;

                    await target.StageBlockFromUriAsync(sourceLink, blockIds[index], new StageBlockFromUriOptions
                    {
                        SourceRange = new HttpRange(offset, Math.Min(_promotionBlockSize, upload.Length - offset)),
                        SourceConditions = new RequestConditions { IfMatch = new ETag(version) }
                    }, token);
                });
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412 || exception.ErrorCode == "CannotVerifyCopySource")
        {
            logger.LogInformation(exception, "Upload {Name} in container {Container} changed or went before it was copied to {Destination}.", upload.Name, StorageContainerNames.Of(container), destination);
            return new BlobPromotion(BlobPromotionResult.UploadChanged, null);
        }

        try
        {
            var labelled = label.ApplyTo(metadata);

            try
            {
                return Promoted(await CommitAsync(target, blockIds, labelled, cancellationToken), labelled);
            }
            catch (RequestFailedException exception) when (exception.Status == 400)
            {
                // The label is for people; the copy is what matters.
                logger.LogWarning(exception, "Storage refused the label for {Name} in container {Container}. Copying it without one.", destination, StorageContainerNames.Of(container));
                return Promoted(await CommitAsync(target, blockIds, metadata, cancellationToken), metadata);
            }
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            return new BlobPromotion(BlobPromotionResult.DestinationExists, null);
        }

        BlobPromotion Promoted(BlobContentInfo written, IReadOnlyDictionary<string, string> writtenMetadata)
        {
            return new BlobPromotion(
                BlobPromotionResult.Promoted,
                new StoredBlob(destination, written.LastModified, upload.Length, written.ETag.ToString(), writtenMetadata));
        }
    }

    public async Task<BlobDeleteResult> DeleteAsync(StorageContainer container, string blobName, string version, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Client(container)
                .GetBlobClient(blobName)
                .DeleteIfExistsAsync(conditions: new BlobRequestConditions { IfMatch = new ETag(version) }, cancellationToken: cancellationToken);

            return response.Value ? BlobDeleteResult.Deleted : BlobDeleteResult.Gone;
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            return BlobDeleteResult.Changed;
        }
    }

    public async Task<bool> WriteLabelAsync(StorageContainer container, StoredBlob blob, BlobLabel label, CancellationToken cancellationToken)
    {
        if (blob.Version is not string version)
        {
            return false;
        }

        try
        {
            await Client(container).GetBlobClient(blob.Name).SetMetadataAsync(
                new Dictionary<string, string>(label.ApplyTo(blob.Metadata)),
                new BlobRequestConditions { IfMatch = new ETag(version) },
                cancellationToken);

            return true;
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            return false;
        }
        catch (RequestFailedException exception) when (exception.Status == 400)
        {
            logger.LogWarning(exception, "Storage refused the label for {Name} in container {Container}.", blob.Name, StorageContainerNames.Of(container));
            return false;
        }
    }

    public async Task<MarkerRead> ReadMarkerAsync(StorageContainer container, CancellationToken cancellationToken)
    {
        try
        {
            var content = (await Client(container).GetBlobClient(StorageBinding.MarkerBlobName).DownloadContentAsync(cancellationToken)).Value;
            var version = content.Details.ETag.ToString();

            try
            {
                return new MarkerRead(ContainerMarker.Parse(content.Content.ToString()), version, false);
            }
            catch (FormatException exception)
            {
                logger.LogError(exception, "The binding marker in container {Container} can't be read.", StorageContainerNames.Of(container));
                return new MarkerRead(null, version, true);
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return new MarkerRead(null, null, false);
        }
    }

    public async Task<bool> WriteMarkerAsync(StorageContainer container, ContainerMarker marker, string? expectedVersion, CancellationToken cancellationToken)
    {
        var options = new BlobUploadOptions
        {
            Conditions = expectedVersion is null
                ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                : new BlobRequestConditions { IfMatch = new ETag(expectedVersion) }
        };

        try
        {
            await Client(container).GetBlobClient(StorageBinding.MarkerBlobName).UploadAsync(BinaryData.FromString(marker.Format()), options, cancellationToken);

            return true;
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task<int?> GetSoftDeleteDaysAsync(CancellationToken cancellationToken)
    {
        try
        {
            var policy = (await blobServiceClient.GetPropertiesAsync(cancellationToken)).Value.DeleteRetentionPolicy;

            return policy is { Enabled: true } ? policy.Days ?? 0 : 0;
        }
        catch (RequestFailedException exception)
        {
            logger.LogWarning(exception, "Could not read the soft delete setting of storage account {Account} ({Status}).", StorageAccount, exception.Status);
            return null;
        }
    }


    /// <summary>Only where nothing is at the destination yet, which is what makes a copy never replace a stored file.</summary>
    private static async Task<BlobContentInfo> CommitAsync(
        BlockBlobClient target,
        IReadOnlyList<string> blockIds,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        return (await target.CommitBlockListAsync(blockIds, new CommitBlockListOptions
        {
            Metadata = new Dictionary<string, string>(metadata),
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, cancellationToken)).Value;
    }

    private BlobContainerClient Client(StorageContainer container)
    {
        return blobServiceClient.GetBlobContainerClient(StorageContainerNames.Of(container));
    }

    private static Dictionary<string, string> Metadata(IDictionary<string, string>? metadata)
    {
        return metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
    }
}
