using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Persistence.Retention;

public interface IRetentionSweeper
{
    /// <param name="today">The date the schedules count from, in the zone the jobs run in.</param>
    /// <param name="now">What a rescheduled mod version's <see cref="ModVersion.Updated"/> moves to.</param>
    Task ScheduleAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken);

    /// <param name="today">Anything scheduled for this date or earlier is due.</param>
    /// <param name="now">What the versions left behind a deleted mod version are stamped with.</param>
    Task DeleteDueAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken);
}
