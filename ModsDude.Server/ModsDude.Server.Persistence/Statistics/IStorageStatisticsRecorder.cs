namespace ModsDude.Server.Persistence.Statistics;

public interface IStorageStatisticsRecorder
{
    /// <summary>
    /// Measures every container and records the result as the samples for <paramref name="date"/>,
    /// replacing any recorded for that date before.
    /// </summary>
    Task RecordAsync(DateOnly date, CancellationToken cancellationToken);
}
