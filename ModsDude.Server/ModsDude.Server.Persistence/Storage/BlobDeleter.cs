using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// Deletes the queued candidates that <see cref="BlobDeletionRule"/> clears, one at a time, recording each
/// one it deletes. Only ever called once the gate has let the run delete.
/// </summary>
public class BlobDeleter(
    ApplicationDbContext dbContext,
    IBlobContainers blobs,
    ITimeService timeService,
    ILogger<BlobDeleter> logger)
    : IBlobDeleter
{
    public async Task<int> DeleteQueuedAsync(StorageMaintenanceSettings settings, CancellationToken cancellationToken)
    {
        var now = timeService.Now();

        var queued = await dbContext.BlobDeletionCandidates
            .AsNoTracking()
            .OrderBy(x => x.Container)
            .ThenBy(x => x.BlobName)
            .ToListAsync(cancellationToken);

        var deleted = 0;

        foreach (var candidate in queued)
        {
            if (await ProcessAsync(candidate, now, settings.MinimumBlobAge, cancellationToken))
            {
                deleted++;
            }
        }

        logger.LogInformation("Deleted {Deleted} of {Queued} queued blobs.", deleted, queued.Count);

        return deleted;
    }

    public Task<bool> DeleteNowAsync(BlobDeletionCandidate candidate, CancellationToken cancellationToken)
    {
        return ProcessAsync(candidate, timeService.Now(), TimeSpan.Zero, cancellationToken);
    }


    private async Task<bool> ProcessAsync(BlobDeletionCandidate candidate, DateTime now, TimeSpan minimumAge, CancellationToken cancellationToken)
    {
        var container = StorageContainerNames.Of(candidate.Container);

        // In this order, never another; see BlobDeletionRule.
        var stored = await blobs.GetAsync(candidate.Container, candidate.BlobName, cancellationToken);
        var isReferenced = await dbContext.IsReferencedAsync(candidate.Container, candidate.BlobName, cancellationToken);
        var isRequeued = !await dbContext.BlobDeletionCandidates.AnyAsync(x => x.Id == candidate.Id && x.Requeued == candidate.Requeued, cancellationToken);

        var decision = BlobDeletionRule.Decide(candidate, stored, isReferenced, isRequeued, now, minimumAge);

        switch (decision.Verdict)
        {
            case BlobDeletionVerdict.Keep:
                logger.LogInformation("Kept {Name} in {Container} for a later run: {Reason}.", candidate.BlobName, container, decision.Reason);
                return false;

            case BlobDeletionVerdict.Drop:
                await DropAsync(candidate, cancellationToken);
                logger.LogInformation("Dropped {Name} in {Container} from the deletion queue: {Reason}.", candidate.BlobName, container, decision.Reason);
                return false;

            case BlobDeletionVerdict.Delete:
                // Not cancelled once sent, nor is recording it below: a delete that goes through with no
                // record is one nobody can find to restore.
                if (!await blobs.DeleteAsync(candidate.Container, candidate.BlobName, stored!.Version!, CancellationToken.None))
                {
                    logger.LogInformation("Kept {Name} in {Container}: it changed or went while it was being deleted.", candidate.BlobName, container);
                    return false;
                }

                try
                {
                    dbContext.DeletedBlobs.Add(new DeletedBlob(candidate, stored, now));
                    await dbContext.SaveChangesAsync(CancellationToken.None);
                    dbContext.ChangeTracker.Clear();
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Deleted {Name} ({Version}) in {Container} but could not record it.", candidate.BlobName, stored.Version, container);
                    throw;
                }

                await DropAsync(candidate, CancellationToken.None);

                logger.LogInformation("Deleted {Name} ({Bytes} bytes) in {Container}, queued as {Reason}.", candidate.BlobName, stored.Length, container, candidate.Reason);
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Verdict, null);
        }
    }

    /// <summary>Only while nothing has queued the blob again since it was read; a requeued row waits for its new delay.</summary>
    private Task<int> DropAsync(BlobDeletionCandidate candidate, CancellationToken cancellationToken)
    {
        return dbContext.BlobDeletionCandidates
            .Where(x => x.Id == candidate.Id && x.Requeued == candidate.Requeued)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
