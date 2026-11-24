using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Statistics;

public class StorageStatisticsRecorder(
    ApplicationDbContext dbContext,
    IBlobContainers blobs,
    ILogger<StorageStatisticsRecorder> logger)
    : IStorageStatisticsRecorder
{
    private const int AdvisoryLockNamespace = 1;


    public async Task RecordAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var tally = new StorageTally();

        foreach (var container in StorageContainerNames.All)
        {
            await foreach (var blob in blobs.ListAsync(container, cancellationToken))
            {
                tally.Add(container, blob);
            }
        }

        var samples = tally.ToSamples(
            date,
            await dbContext.ModVersions.GetRegisteredBytesPerRepoAsync(cancellationToken),
            await dbContext.SavegameSnapshots.GetRegisteredBytesPerRepoAsync(cancellationToken));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Concurrent runs for one day otherwise interleave their inserts and deadlock on the unique index.
        await dbContext.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockNamespace}, {date.DayNumber})",
            cancellationToken);

        await dbContext.StorageUsageSamples
            .Where(x => x.Date == date)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.StorageUsageSamples.AddRange(samples);
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Recorded {Count} storage usage samples for {Date}.", samples.Count, date);
    }
}
