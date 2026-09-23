namespace ModsDude.Server.Api.Maintenance;

public class RetentionOptions
{
    public const string SectionName = "Retention";


    /// <summary>
    /// Whether the two jobs are registered at all. Off removes them from Hangfire rather than leaving
    /// stale definitions behind, so turning it back on is all it takes to resume.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The zone the jobs' times and "today" are read in. A deletion date is a date, so which day it is
    /// has to be decided somewhere, and the people reading the dates are the ones it should match.
    /// </summary>
    public string TimeZone { get; set; } = "Europe/Stockholm";

    /// <summary>
    /// When the scheduling job runs. Before the deletion job, so a row that became eligible
    /// yesterday is dated before anything is deleted - not that it matters to it, since a newly
    /// scheduled row is never due the same day.
    /// </summary>
    public string ScheduleCron { get; set; } = "0 5 * * *";

    /// <summary>When the deletion job runs, and so the time of day every deletion date means.</summary>
    public string DeleteCron { get; set; } = "0 6 * * *";
}
