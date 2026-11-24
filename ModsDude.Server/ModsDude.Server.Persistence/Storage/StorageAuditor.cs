using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// Compares every container with the database, replaces the findings with what it found, and labels
/// referenced blobs that carry no label yet. It deletes nothing.
/// </summary>
public class StorageAuditor(
    ApplicationDbContext dbContext,
    IBlobContainers blobs,
    ILogger<StorageAuditor> logger)
    : IStorageAuditor
{
    public async Task<StorageAuditOutcome> AuditAsync(bool mayLabel, CancellationToken cancellationToken)
    {
        var findings = new List<StorageAuditFinding>();
        var labelled = 0;

        foreach (var container in StorageContainerNames.All)
        {
            var stored = new List<StoredBlob>();
            await foreach (var blob in blobs.ListAsync(container, cancellationToken))
            {
                stored.Add(blob);
            }

            // After the listing, so a blob registered while it ran reads as referenced.
            var referenced = await dbContext.GetReferencedNamesAsync(container, cancellationToken);
            var result = BlobAudit.Classify(container, stored, referenced);

            findings.AddRange(result.Unreferenced.Select(x => StorageAuditFinding.Stored(container, StorageFindingKind.Unreferenced, x)));
            findings.AddRange(result.Unrecognised.Select(x => StorageAuditFinding.Stored(container, StorageFindingKind.Unrecognised, x)));

            // A name referred to after the listing ran looks missing, so each is asked about on its own.
            foreach (var name in result.Missing)
            {
                if (await blobs.GetAsync(container, name, cancellationToken) is null)
                {
                    logger.LogError("Blob {Name} in {Container} is referred to by the database and is not stored.", name, StorageContainerNames.Of(container));
                    findings.Add(StorageAuditFinding.Missing(container, name));
                }
            }

            if (mayLabel && result.Unlabelled.Count > 0)
            {
                labelled += await LabelAsync(container, result.Unlabelled, cancellationToken);
            }
        }

        await ReplaceFindingsAsync(findings, cancellationToken);

        var outcome = new StorageAuditOutcome(
            findings.Count(x => x.Kind is StorageFindingKind.Unreferenced),
            findings.Count(x => x.Kind is StorageFindingKind.Missing),
            findings.Count(x => x.Kind is StorageFindingKind.Unrecognised),
            labelled);

        logger.LogInformation(
            "Storage audit found {Unreferenced} unreferenced, {Missing} missing and {Unrecognised} unrecognised blobs, and labelled {Labelled}.",
            outcome.Unreferenced, outcome.Missing, outcome.Unrecognised, outcome.Labelled);

        return outcome;
    }


    private async Task<int> LabelAsync(StorageContainer container, IReadOnlyList<StoredBlob> unlabelled, CancellationToken cancellationToken)
    {
        var labels = await dbContext.GetLabelsAsync(container, [.. unlabelled.Select(x => x.Name)], cancellationToken);
        var labelled = 0;

        foreach (var blob in unlabelled)
        {
            // Changed since the listing, or no longer referred to: the next audit sees it as it is then.
            if (labels.TryGetValue(blob.Name, out var label) && await blobs.WriteLabelAsync(container, blob, label, cancellationToken))
            {
                labelled++;
            }
        }

        return labelled;
    }

    private async Task ReplaceFindingsAsync(IReadOnlyList<StorageAuditFinding> findings, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.StorageAuditFindings.ExecuteDeleteAsync(cancellationToken);

        dbContext.StorageAuditFindings.AddRange(findings);
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        dbContext.ChangeTracker.Clear();
    }
}


public sealed record StorageAuditOutcome(int Unreferenced, int Missing, int Unrecognised, int Labelled);
