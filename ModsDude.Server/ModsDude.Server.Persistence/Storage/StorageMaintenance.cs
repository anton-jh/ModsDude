using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// One storage maintenance run: inspect, audit, and delete what is queued, in that order and only as far
/// as the gate allows. Also the admin page's way in.
/// </summary>
/// <remarks>
/// <para>
/// A run holds a session advisory lock, so two runs never interleave, however they were started. A run
/// that finds the lock taken does nothing.
/// </para>
/// <para>
/// Nothing is deleted unless the storage is this database's, storage keeps deleted blobs long enough,
/// and nothing the database refers to is missing. A missing blob means something deleted what it should
/// not have, or that storage and the database disagree; either way, deleting more is the wrong answer.
/// The generation advances before and after the deletes, so a database restored from a backup taken
/// during them is behind the markers.
/// </para>
/// </remarks>
public class StorageMaintenance(
    ApplicationDbContext dbContext,
    IStorageGatekeeper gatekeeper,
    IStorageAuditor auditor,
    IBlobDeleter deleter,
    ITimeService timeService,
    ILogger<StorageMaintenance> logger)
    : IStorageMaintenance
{
    public async Task<StorageMaintenanceOutcome> RunAsync(StorageMaintenanceSettings settings, CancellationToken cancellationToken)
    {
        await using var runLock = await TryLockAsync(cancellationToken);

        if (runLock is null)
        {
            logger.LogInformation("Another storage maintenance run is in progress; this one does nothing.");
            return new StorageMaintenanceOutcome(["Another run was in progress."], 0, true);
        }

        var refusals = new List<string>();
        var deleted = 0;
        var audited = false;

        try
        {
            var inspection = await gatekeeper.InspectAsync(settings.MinimumSoftDeleteDays, cancellationToken);
            refusals.AddRange(inspection.Decision.WriteRefusals);

            await PruneDeletedBlobsAsync(settings.DeletedBlobRetentionDays, inspection.SoftDeleteDays, cancellationToken);

            if (inspection.Decision.MayWrite)
            {
                refusals.AddRange(await gatekeeper.PrepareAsync(inspection, cancellationToken));
            }

            var mayWrite = refusals.Count == 0;

            var audit = await auditor.AuditAsync(mayWrite, cancellationToken);
            audited = true;

            if (mayWrite)
            {
                refusals.AddRange(inspection.Decision.DeleteRefusals);

                if (audit.Missing > 0)
                {
                    refusals.Add($"{audit.Missing} blobs the database refers to are missing.");
                }
            }

            if (refusals.Count == 0)
            {
                await gatekeeper.AdvanceAsync(cancellationToken);

                var queued = await dbContext.BlobDeletionCandidates
                    .AsNoTracking()
                    .OrderBy(x => x.Container)
                    .ThenBy(x => x.BlobName)
                    .ToListAsync(cancellationToken);

                foreach (var candidate in queued)
                {
                    if (await deleter.DeleteAsync(candidate, settings.MinimumBlobAge, cancellationToken) == BlobDeletionResult.Deleted)
                    {
                        deleted++;
                    }
                }

                logger.LogInformation("Deleted {Deleted} of {Queued} queued blobs.", deleted, queued.Count);

                await gatekeeper.AdvanceAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Storage maintenance failed after deleting {Deleted} blobs.", deleted);
            refusals.Add("The run failed. See the log.");
            await RecordAsync(audited, refusals, deleted, CancellationToken.None);
            throw;
        }

        foreach (var refusal in refusals)
        {
            logger.LogError("Storage maintenance refused to delete: {Refusal}", refusal);
        }

        await RecordAsync(audited, refusals, deleted, cancellationToken);

        return new StorageMaintenanceOutcome(refusals, deleted, false);
    }

    public Task<StorageInspection> InspectAsync(int minimumSoftDeleteDays, CancellationToken cancellationToken)
    {
        return gatekeeper.InspectAsync(minimumSoftDeleteDays, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> BindByHandAsync(string operatorName, CancellationToken cancellationToken)
    {
        await using var runLock = await TryLockAsync(cancellationToken);

        return runLock is null
            ? ["A maintenance run is in progress. Try again when it is done."]
            : await gatekeeper.BindByHandAsync(operatorName, cancellationToken);
    }

    public async Task<ReleasedModFileDeletion> DeleteReleasedModFileAsync(
        RepoId repoId,
        ModId modId,
        ModVersionId versionId,
        int minimumSoftDeleteDays,
        CancellationToken cancellationToken)
    {
        var blobName = BlobNames.Mod(repoId, modId, versionId);

        var candidate = await dbContext.BlobDeletionCandidates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Container == StorageContainer.Mods && x.BlobName == blobName, cancellationToken);

        if (candidate is not { Reason: BlobDeletionReason.ReferenceRemoved })
        {
            return ReleasedModFileDeletion.NotReleased;
        }

        await using var runLock = await TryLockAsync(cancellationToken);

        if (runLock is null)
        {
            logger.LogInformation("Kept the file of deleted mod version {Name}: a maintenance run is in progress.", blobName);
            return ReleasedModFileDeletion.Refused;
        }

        var refusals = await RefusalsToDeleteNowAsync(minimumSoftDeleteDays, cancellationToken);

        if (refusals.Count > 0)
        {
            logger.LogWarning("Kept the file of deleted mod version {Name}: {Refusals}", blobName, string.Join(" ", refusals));
            return ReleasedModFileDeletion.Refused;
        }

        await gatekeeper.AdvanceAsync(cancellationToken);

        BlobDeletionResult result;
        await using (await dbContext.LockBlobAsync(StorageContainer.Mods, blobName, cancellationToken))
        {
            result = await deleter.DeleteAsync(candidate, TimeSpan.Zero, cancellationToken);
        }

        await gatekeeper.AdvanceAsync(cancellationToken);

        return result switch
        {
            BlobDeletionResult.Deleted or BlobDeletionResult.Gone => ReleasedModFileDeletion.Deleted,
            BlobDeletionResult.Dropped => ReleasedModFileDeletion.Registered,
            BlobDeletionResult.Kept => ReleasedModFileDeletion.Refused,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };
    }

    public async Task<QueuedDeletions> QueueForDeletionAsync(IReadOnlyCollection<long> findingIds, string operatorName, CancellationToken cancellationToken)
    {
        var findings = await dbContext.StorageAuditFindings
            .AsNoTracking()
            .Where(x => findingIds.Contains(x.Id) && x.Kind == StorageFindingKind.Unreferenced && x.Version != null)
            .OrderBy(x => x.Container)
            .ThenBy(x => x.BlobName)
            .ToListAsync(cancellationToken);

        foreach (var finding in findings)
        {
            await dbContext.QueueAdminAsync(finding.Container, finding.BlobName, finding.Version!, operatorName, cancellationToken);
        }

        logger.LogWarning(
            "Admin {Operator} queued {Count} blobs ({Bytes} bytes) for deletion: {Names}",
            operatorName, findings.Count, findings.Sum(x => x.SizeBytes), string.Join(", ", findings.Select(x => $"{x.Container}/{x.BlobName}")));

        return new QueuedDeletions(findings.Count, findings.Sum(x => x.SizeBytes));
    }


    /// <summary>What a run would refuse over, judged from the gate now and from the last run's audit.</summary>
    private async Task<IReadOnlyList<string>> RefusalsToDeleteNowAsync(int minimumSoftDeleteDays, CancellationToken cancellationToken)
    {
        var decision = (await gatekeeper.InspectAsync(minimumSoftDeleteDays, cancellationToken)).Decision;
        var refusals = new List<string>([.. decision.WriteRefusals, .. decision.DeleteRefusals]);

        if (decision.CreateBinding || decision.RecordStorageAccount || decision.ToMark.Count > 0 || decision.ToUpgrade.Count > 0)
        {
            refusals.Add("Storage needs a maintenance run to prepare it.");
        }

        var status = await dbContext.StorageMaintenanceStatuses.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        if (status is not { LastAudit: not null, Refusals.Count: 0 })
        {
            refusals.Add("The last maintenance run was not free to delete.");
        }

        return refusals;
    }

    /// <summary>
    /// Forgets deleted blobs once storage can no longer give them back: after the longer of the setting
    /// and storage's own soft delete period. Nothing is forgotten while that period can't be read.
    /// </summary>
    private async Task PruneDeletedBlobsAsync(int retentionDays, int? softDeleteDays, CancellationToken cancellationToken)
    {
        if (softDeleteDays is not int storageDays)
        {
            return;
        }

        var cutoff = timeService.Now().AddDays(-Math.Max(retentionDays, storageDays));
        var pruned = await dbContext.DeletedBlobs.Where(x => x.Deleted < cutoff).ExecuteDeleteAsync(cancellationToken);

        if (pruned > 0)
        {
            logger.LogInformation("Forgot {Count} blobs deleted before {Cutoff}.", pruned, cutoff);
        }
    }

    private async Task RecordAsync(bool audited, IReadOnlyList<string> refusals, int deleted, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();

        var status = await dbContext.StorageMaintenanceStatuses.SingleOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            status = new StorageMaintenanceStatus();
            dbContext.StorageMaintenanceStatuses.Add(status);
        }

        status.RecordRun(timeService.Now(), audited, refusals, deleted);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <returns>The held lock, or null where another run holds it.</returns>
    private async Task<RunLock?> TryLockAsync(CancellationToken cancellationToken)
    {
        // Session level, so the connection stays open for the whole run.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        var acquired = await dbContext.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_lock({AdvisoryLockSpaces.StorageMaintenanceRun}, 0) AS \"Value\"")
            .SingleAsync(cancellationToken);

        if (acquired)
        {
            return new RunLock(dbContext);
        }

        await dbContext.Database.CloseConnectionAsync();
        return null;
    }


    private sealed class RunLock(ApplicationDbContext dbContext) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({AdvisoryLockSpaces.StorageMaintenanceRun}, 0)", CancellationToken.None);
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}


/// <param name="Refusals">Why the run deleted nothing. Empty when it was free to.</param>
/// <param name="WasBusy">Whether another run held the lock, so this one did nothing at all.</param>
public sealed record StorageMaintenanceOutcome(IReadOnlyList<string> Refusals, int Deleted, bool WasBusy);

public sealed record QueuedDeletions(int Count, long Bytes);

public enum ReleasedModFileDeletion
{
    /// <summary>The file is gone.</summary>
    Deleted,

    /// <summary>The file is not one a deleted mod version left behind.</summary>
    NotReleased,

    /// <summary>The file was one a deleted mod version left behind, and a version refers to it again.</summary>
    Registered,

    /// <summary>The file is one a deleted mod version left behind, and it stays for now.</summary>
    Refused
}
