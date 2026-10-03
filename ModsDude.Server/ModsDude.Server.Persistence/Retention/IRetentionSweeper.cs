using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Retention;

public interface IRetentionSweeper
{
    Task ScheduleAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken);

    Task DeleteDueAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken);

    Task<PruneResult> PruneNowAsync(RepoId repoId, PrunableHistory history, DateTimeOffset now, CancellationToken cancellationToken);
}
