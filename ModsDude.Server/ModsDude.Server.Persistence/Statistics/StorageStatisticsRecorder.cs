using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Statistics;

public class StorageStatisticsRecorder(
    ApplicationDbContext dbContext,
    IModStorageService modStorage,
    ISavegameStorageService savegameStorage,
    IModImageStorageService imageStorage,
    ILogger<StorageStatisticsRecorder> logger)
    : IStorageStatisticsRecorder
{
    public async Task RecordAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var tally = new StorageTally();

        await AddAsync(tally, StorageContainer.Mods, modStorage.ListStoredMods(cancellationToken));
        await AddAsync(tally, StorageContainer.Savegames, savegameStorage.ListStoredSavegames(cancellationToken));
        await AddAsync(tally, StorageContainer.Images, imageStorage.ListStoredImages(cancellationToken));

        var samples = tally.ToSamples(
            date,
            await dbContext.ModVersions.GetRegisteredBytesPerRepoAsync(cancellationToken),
            await dbContext.SavegameSnapshots.GetRegisteredBytesPerRepoAsync(cancellationToken));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.StorageUsageSamples
            .Where(x => x.Date == date)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.StorageUsageSamples.AddRange(samples);
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Recorded {Count} storage usage samples for {Date}.", samples.Count, date);
    }


    private static async Task AddAsync(StorageTally tally, StorageContainer container, IAsyncEnumerable<StoredBlob> blobs)
    {
        await foreach (var blob in blobs)
        {
            tally.Add(container, blob);
        }
    }
}
