using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Retention;

public interface IRetentionSweeper
{
    /// <param name="today">The date the schedules count from, in the zone the jobs run in.</param>
    /// <param name="now">What a rescheduled mod version's <see cref="ModVersion.Updated"/> moves to.</param>
    Task ScheduleAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken);

    /// <param name="today">Anything scheduled for this date or earlier is due.</param>
    /// <param name="now">What the versions left behind a deleted mod version are stamped with.</param>
    Task DeleteDueAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one kind of row in one repo that is outside its window and that nothing holds, without
    /// waiting for a schedule. Rows only eligible as winding down are left to the jobs: that reason
    /// means a history nobody has used for a while, which only the grace period can tell. So a rerun
    /// finds nothing new to delete.
    /// </summary>
    /// <param name="now">What the versions left behind a deleted mod version are stamped with.</param>
    Task<PruneResult> PruneNowAsync(RepoId repoId, PrunableHistory history, DateTimeOffset now, CancellationToken cancellationToken);
}
