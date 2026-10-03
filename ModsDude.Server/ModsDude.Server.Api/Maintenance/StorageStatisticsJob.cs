using Hangfire;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Persistence.Statistics;

namespace ModsDude.Server.Api.Maintenance;

/// <summary>
/// The daily storage sample as Hangfire runs it. The work lives in <see cref="IStorageStatisticsRecorder"/>.
/// </summary>
public class StorageStatisticsJob(
    IStorageStatisticsRecorder recorder,
    ITimeService timeService)
{
    public const string JobId = "storage-statistics";


    public static void Register(IRecurringJobManager recurringJobs, StorageStatisticsOptions options)
    {
        recurringJobs.AddOrUpdate<StorageStatisticsJob>(JobId, x => x.RecordAsync(CancellationToken.None), options.Cron);
    }


    // A rerun replaces the day's samples, so a retry or a second run by hand is harmless.
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public Task RecordAsync(CancellationToken cancellationToken)
    {
        return recorder.RecordAsync(DateOnly.FromDateTime(timeService.Now()), cancellationToken);
    }
}
