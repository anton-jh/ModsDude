using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Admin;

/// <summary>
/// The latest storage samples, and the earliest ones within <see cref="AdminWindows.StorageChangeDays"/>
/// before them to measure change against.
/// </summary>
public record StorageComparison(
    DateOnly Latest,
    DateOnly Baseline,
    IReadOnlyList<StorageUsageSample> LatestSamples,
    IReadOnlyList<StorageUsageSample> BaselineSamples)
{
    public static async Task<StorageComparison?> LoadAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await dbContext.StorageUsageSamples.GetLatestDateAsync(cancellationToken) is not DateOnly latest)
        {
            return null;
        }

        var baseline = await dbContext.StorageUsageSamples.GetEarliestDateFromAsync(latest.AddDays(-AdminWindows.StorageChangeDays), cancellationToken)
            ?? latest;

        return new StorageComparison(
            latest,
            baseline,
            await dbContext.StorageUsageSamples.GetOnDateAsync(latest, cancellationToken),
            await dbContext.StorageUsageSamples.GetOnDateAsync(baseline, cancellationToken));
    }


    public bool HasBaseline => Baseline != Latest;


    public RepoStorage ForRepo(RepoId repoId)
    {
        var stored = LatestSamples.Where(x => x.RepoId == repoId).Sum(x => x.StoredBytes);
        var baselineStored = BaselineSamples.Where(x => x.RepoId == repoId).Sum(x => x.StoredBytes);

        return new RepoStorage(stored, HasBaseline ? stored - baselineStored : null);
    }
}


/// <param name="ChangeBytes">How much <paramref name="StoredBytes"/> grew since the baseline, or <c>null</c> without one.</param>
public record RepoStorage(long StoredBytes, long? ChangeBytes);
