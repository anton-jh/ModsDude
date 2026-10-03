using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Statistics;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class StorageUsageSampleExtensions
{
    public static async Task<DateOnly?> GetLatestDateAsync(this DbSet<StorageUsageSample> dbSet, CancellationToken cancellationToken)
    {
        return await dbSet
            .AsNoTracking()
            .MaxAsync(x => (DateOnly?)x.Date, cancellationToken);
    }

    /// <summary>The earliest date sampled on or after <paramref name="from"/>.</summary>
    public static async Task<DateOnly?> GetEarliestDateFromAsync(this DbSet<StorageUsageSample> dbSet, DateOnly from, CancellationToken cancellationToken)
    {
        return await dbSet
            .AsNoTracking()
            .Where(x => x.Date >= from)
            .MinAsync(x => (DateOnly?)x.Date, cancellationToken);
    }

    public static Task<List<StorageUsageSample>> GetOnDateAsync(this DbSet<StorageUsageSample> dbSet, DateOnly date, CancellationToken cancellationToken)
    {
        return dbSet
            .AsNoTracking()
            .Where(x => x.Date == date)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Stored bytes per container per day from <paramref name="from"/> on, oldest first.</summary>
    public static async Task<List<StorageDailyTotal>> GetDailyTotalsFromAsync(this DbSet<StorageUsageSample> dbSet, DateOnly from, CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .AsNoTracking()
            .Where(x => x.Date >= from)
            .GroupBy(x => new { x.Date, x.Container })
            .Select(x => new { x.Key.Date, x.Key.Container, StoredBytes = x.Sum(y => y.StoredBytes) })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Select(x => new StorageDailyTotal(x.Date, x.Container, x.StoredBytes))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Container)];
    }
}


public record StorageDailyTotal(DateOnly Date, StorageContainer Container, long StoredBytes);
