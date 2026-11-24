namespace ModsDude.Server.Api.Maintenance;

public class StorageMaintenanceOptions
{
    public const string SectionName = "StorageMaintenance";


    public bool Enabled { get; set; } = true;

    /// <summary>When the run starts, in UTC.</summary>
    public string Cron { get; set; } = "0 4 * * *";

    /// <summary>
    /// How long a blob must have sat untouched before it may be deleted. Keeps the run away from an
    /// upload that has not been registered yet: an upload link lives 30 minutes, and registering follows
    /// the upload at once.
    /// </summary>
    public TimeSpan MinimumBlobAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>The fewest days the storage account must keep deleted blobs for the run to delete anything.</summary>
    public int MinimumSoftDeleteDays { get; set; } = 14;
}
