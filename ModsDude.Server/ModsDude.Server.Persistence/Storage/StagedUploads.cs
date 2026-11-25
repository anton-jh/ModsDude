using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <remarks>
/// <para>
/// Upload links name an upload's own blob and never an address, because a link can overwrite whatever it
/// names for as long as it lasts. The copy into place only ever creates a blob, so a stored file is never
/// replaced.
/// </para>
/// <para>
/// A blob already at the address is claimed, so a deletion run that read it before cannot delete it. A
/// copy is a new blob, which a run never deletes before it is old enough. The address is queued before the
/// copy, so a copy whose row never commits is collected.
/// </para>
/// </remarks>
public class StagedUploads(
    ApplicationDbContext dbContext,
    IBlobContainers blobs,
    ILogger<StagedUploads> logger)
    : IStagedUploads
{
    public Task<StagedUploadResult> StoreModAsync(
        RepoId repoId, ModId modId, ModVersionId versionId, UserId userId, UploadId? uploadId,
        string contentHash, ModBlobLabel label, CancellationToken cancellationToken)
    {
        return StoreAsync(
            StorageContainer.Mods,
            BlobNames.Mod(repoId, modId, versionId),
            repoId,
            userId,
            uploadId,
            stored => stored.RecordedContentHash == contentHash ? null : StagedUploadOutcome.HashMismatch,
            upload => upload.RecordedContentHash == contentHash ? null : StagedUploadOutcome.HashMismatch,
            label,
            cancellationToken);
    }

    public Task<StagedUploadResult> StoreSavegameAsync(
        RepoId repoId, SavegameId savegameId, string contentHash, UserId userId, UploadId? uploadId,
        long sizeBytes, SavegameBlobLabel label, CancellationToken cancellationToken)
    {
        // The address is the hash of the save's contents, which only unpacking the archive can check, so
        // a save already stored is taken as it is. An upload is held to what its own uploader says of it.
        return StoreAsync(
            StorageContainer.Savegames,
            BlobNames.Savegame(repoId, savegameId, contentHash),
            repoId,
            userId,
            uploadId,
            _ => null,
            upload => upload.Length == sizeBytes && Sha256Hex.IsValid(upload.RecordedContentHash) ? null : StagedUploadOutcome.UploadInvalid,
            label,
            cancellationToken);
    }


    /// <param name="refuseStored">Why the file already at the address can't be used, or null where it can.</param>
    /// <param name="refuseUpload">Why the upload can't be copied there, or null where it can.</param>
    private async Task<StagedUploadResult> StoreAsync(
        StorageContainer container,
        string address,
        RepoId repoId,
        UserId userId,
        UploadId? uploadId,
        Func<StoredBlob, StagedUploadOutcome?> refuseStored,
        Func<StoredBlob, StagedUploadOutcome?> refuseUpload,
        BlobLabel label,
        CancellationToken cancellationToken)
    {
        if (await ClaimAsync(container, address, label, refuseStored, cancellationToken) is { } claimed)
        {
            return claimed;
        }

        if (uploadId is not UploadId id
            || await blobs.GetAsync(container, BlobNames.Upload(repoId, userId, id), cancellationToken) is not { } upload)
        {
            return new StagedUploadResult(StagedUploadOutcome.Missing, null);
        }

        if (refuseUpload(upload) is { } refusal)
        {
            return new StagedUploadResult(refusal, upload);
        }

        await dbContext.QueueUploadAsync(container, address, repoId, userId, cancellationToken);

        // The recorded hash is all that is carried over: whatever else the uploader wrote is theirs, and
        // the label is the server's.
        var metadata = upload.RecordedContentHash is string hash
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [StoredBlob.ContentHashKey] = hash }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var promotion = await blobs.PromoteAsync(container, upload, address, metadata, label, cancellationToken);

        switch (promotion.Result)
        {
            case BlobPromotionResult.Promoted:
                logger.LogInformation("Copied upload {Upload} to {Address} in {Container}.", upload.Name, address, StorageContainerNames.Of(container));
                return new StagedUploadResult(StagedUploadOutcome.Stored, promotion.Stored);

            case BlobPromotionResult.UploadChanged:
                return new StagedUploadResult(StagedUploadOutcome.UploadChanged, null);

            case BlobPromotionResult.DestinationExists:
                // Another copy of the same file got there first.
                return await ClaimAsync(container, address, label, refuseStored, cancellationToken)
                    ?? new StagedUploadResult(StagedUploadOutcome.UploadChanged, null);

            default:
                throw new ArgumentOutOfRangeException(nameof(promotion), promotion.Result, null);
        }
    }

    /// <returns>The outcome of using the file at the address, or null where there is none.</returns>
    private async Task<StagedUploadResult?> ClaimAsync(
        StorageContainer container,
        string address,
        BlobLabel label,
        Func<StoredBlob, StagedUploadOutcome?> refuseStored,
        CancellationToken cancellationToken)
    {
        if (await blobs.ClaimAsync(container, address, label, cancellationToken) is not { } stored)
        {
            return null;
        }

        return new StagedUploadResult(refuseStored(stored) ?? StagedUploadOutcome.Stored, stored);
    }
}
