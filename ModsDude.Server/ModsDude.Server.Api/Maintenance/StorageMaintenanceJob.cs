using Hangfire;
using Microsoft.Extensions.Options;
using ModsDude.Server.Persistence.Storage;

namespace ModsDude.Server.Api.Maintenance;

/// <summary>
/// The storage maintenance run as Hangfire runs it: daily, and whenever the admin page asks. The work
/// lives in <see cref="IStorageMaintenance"/>.
/// </summary>
public class StorageMaintenanceJob(
    IStorageMaintenance maintenance,
    IOptions<StorageMaintenanceOptions> options,
    ILogger<StorageMaintenanceJob> logger)
{
    public const string JobId = "storage-maintenance";

    /// <summary>A recurring job id with no job behind it. Removed so Hangfire never tries to run it.</summary>
    private const string _removedJobId = "blob-reclamation";


    /// <summary>
    /// Registers the job, or removes it when maintenance is disabled. Idempotent, like
    /// <see cref="RetentionJobs.Register"/>.
    /// </summary>
    /// <remarks>
    /// An occurrence missed while the server was down is skipped rather than caught up on at startup,
    /// so the run only ever starts at its scheduled time and a crash loop cannot become a delete loop.
    /// </remarks>
    public static void Register(IRecurringJobManager recurringJobs, StorageMaintenanceOptions options)
    {
        recurringJobs.RemoveIfExists(_removedJobId);

        if (!options.Enabled)
        {
            recurringJobs.RemoveIfExists(JobId);
            return;
        }

        var jobOptions = new RecurringJobOptions
        {
            MisfireHandling = MisfireHandlingMode.Ignorable
        };

        recurringJobs.AddOrUpdate<StorageMaintenanceJob>(JobId, x => x.RunAsync(CancellationToken.None), options.Cron, jobOptions);
    }


    // Not retried: a run that fails deleted less than it could have, which is the harmless direction,
    // and the next one picks up where it stopped.
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Checked here as well as at registration: removing the recurring job leaves one already
        // queued, or started from the admin page, to run.
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Storage maintenance is disabled; skipping the run.");
            return;
        }

        var settings = new StorageMaintenanceSettings(
            options.Value.MinimumBlobAge, options.Value.MinimumSoftDeleteDays, options.Value.DeletedBlobRetentionDays);
        var outcome = await maintenance.RunAsync(settings, cancellationToken);

        // Failed rather than returned from, so a refusal shows in the dashboard as well as on the admin page.
        if (!outcome.WasBusy && outcome.Refusals.Count > 0)
        {
            throw new InvalidOperationException($"Storage maintenance deleted nothing: {string.Join(" ", outcome.Refusals)}");
        }
    }
}
