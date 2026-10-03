namespace ModsDude.Server.Api.Maintenance;

public class StorageStatisticsOptions
{
    public const string SectionName = "StorageStatistics";


    /// <summary>When the daily storage sample is taken, in UTC.</summary>
    public string Cron { get; set; } = "30 3 * * *";
}
