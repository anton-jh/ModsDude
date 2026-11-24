using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// Deletes one queued candidate where <see cref="BlobDeletionRule"/> clears it, and records the delete.
/// Only ever called once the gate has let the caller delete.
/// </summary>
public class BlobDeleter(
    ApplicationDbContext dbContext,
    IBlobContainers blobs,
    ITimeService timeService,
    ILogger<BlobDeleter> logger)
    : IBlobDeleter
{
    public async Task<BlobDeletionResult> DeleteAsync(BlobDeletionCandidate candidate, TimeSpan minimumAge, CancellationToken cancellationToken)
    {
        var container = StorageContainerNames.Of(candidate.Container);

        // In this order, never another; see BlobDeletionRule.
        var stored = await blobs.GetAsync(candidate.Container, candidate.BlobName, cancellationToken);
        var isReferenced = await dbContext.IsReferencedAsync(candidate.Container, candidate.BlobName, cancellationToken);
        var isRequeued = !await dbContext.BlobDeletionCandidates.AnyAsync(x => x.Id == candidate.Id && x.Requeued == candidate.Requeued, cancellationToken);

        var decision = BlobDeletionRule.Decide(candidate, stored, isReferenced, isRequeued, timeService.Now(), minimumAge);

        switch (decision.Verdict)
        {
            case BlobDeletionVerdict.Keep:
                logger.LogInformation("Kept {Name} in {Container} for a later run: {Reason}.", candidate.BlobName, container, decision.Reason);
                return BlobDeletionResult.Kept;

            case BlobDeletionVerdict.Drop:
                await DropAsync(candidate, cancellationToken);
                logger.LogInformation("Dropped {Name} in {Container} from the deletion queue: {Reason}.", candidate.BlobName, container, decision.Reason);
                return stored is null ? BlobDeletionResult.Gone : BlobDeletionResult.Dropped;

            case BlobDeletionVerdict.Delete:
                // Not cancelled once sent, nor is recording it below: a delete that goes through with no
                // record is one nobody can find to restore.
                var deletion = await blobs.DeleteAsync(candidate.Container, candidate.BlobName, stored!.Version!, CancellationToken.None);

                if (deletion == BlobDeleteResult.Changed)
                {
                    logger.LogInformation("Kept {Name} in {Container}: it changed while it was being deleted.", candidate.BlobName, container);
                    return BlobDeletionResult.Kept;
                }

                // Gone as well as deleted: storage answers a retried delete whose first answer was lost
                // as if the blob had never been there, and nothing else deletes blobs.
                await RecordAsync(candidate, stored, container);
                await DropAsync(candidate, CancellationToken.None);

                logger.LogInformation(
                    "Deleted {Name} ({Bytes} bytes) in {Container}, queued as {Reason}{Gone}.",
                    candidate.BlobName, stored.Length, container, candidate.Reason, deletion == BlobDeleteResult.Gone ? "; storage said it was already gone" : "");
                return BlobDeletionResult.Deleted;

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Verdict, null);
        }
    }


    private async Task RecordAsync(BlobDeletionCandidate candidate, StoredBlob stored, string container)
    {
        var record = new DeletedBlob(candidate, stored, timeService.Now());

        try
        {
            dbContext.DeletedBlobs.Add(record);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Deleted {Name} ({Version}) in {Container} but could not record it.", candidate.BlobName, stored.Version, container);
            throw;
        }
        finally
        {
            dbContext.Entry(record).State = EntityState.Detached;
        }
    }

    /// <summary>Only while nothing has queued the blob again since it was read; a requeued row waits for the next run.</summary>
    private Task<int> DropAsync(BlobDeletionCandidate candidate, CancellationToken cancellationToken)
    {
        return dbContext.BlobDeletionCandidates
            .Where(x => x.Id == candidate.Id && x.Requeued == candidate.Requeued)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
