using Hangfire;
using Microsoft.Extensions.Options;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Persistence.Retention;

namespace ModsDude.Server.Api.Maintenance;

/// <summary>
/// The two daily retention jobs as Hangfire runs them. Everything they decide lives in
/// <see cref="RetentionSweeper"/>; this only works out what day it is where the jobs run.
/// </summary>
public class RetentionJobs(
    RetentionSweeper sweeper,
    ITimeService timeService,
    IOptions<RetentionOptions> options)
{
    public const string ScheduleJobId = "retention-schedule";
    public const string DeleteJobId = "retention-delete";


    /// <summary>
    /// Registers both jobs, or removes them when retention is disabled. Idempotent, so it runs on
    /// every start and a changed time in configuration simply replaces the old one.
    /// </summary>
    public static void Register(IRecurringJobManager recurringJobs, RetentionOptions options)
    {
        if (!options.Enabled)
        {
            recurringJobs.RemoveIfExists(ScheduleJobId);
            recurringJobs.RemoveIfExists(DeleteJobId);
            return;
        }

        var jobOptions = new RecurringJobOptions
        {
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone)
        };

        recurringJobs.AddOrUpdate<RetentionJobs>(ScheduleJobId, x => x.ScheduleAsync(CancellationToken.None), options.ScheduleCron, jobOptions);
        recurringJobs.AddOrUpdate<RetentionJobs>(DeleteJobId, x => x.DeleteAsync(CancellationToken.None), options.DeleteCron, jobOptions);
    }


    // One at a time, and not retried. A second run started while one is going would schedule the
    // same rows twice over, and a failed run is simply tomorrow's to finish - retrying a deletion job
    // an hour later would move the time of day a deletion date means.
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 0)]
    public Task ScheduleAsync(CancellationToken cancellationToken)
    {
        var (today, now) = Now();

        return sweeper.ScheduleAsync(today, now, cancellationToken);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 0)]
    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        var (today, now) = Now();

        return sweeper.DeleteDueAsync(today, now, cancellationToken);
    }


    private (DateOnly Today, DateTimeOffset Now) Now()
    {
        DateTimeOffset now = timeService.Now();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

        return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime), now);
    }
}
