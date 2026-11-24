namespace ModsDude.Server.Persistence.Storage;

/// <param name="MinimumBlobAge">How long a blob must have sat untouched before it may be deleted.</param>
/// <param name="MinimumSoftDeleteDays">The fewest days storage must keep deleted blobs for anything to be deleted.</param>
/// <param name="DeletedBlobRetentionDays">The fewest days a deleted blob stays listed, however briefly storage keeps it.</param>
public sealed record StorageMaintenanceSettings(TimeSpan MinimumBlobAge, int MinimumSoftDeleteDays, int DeletedBlobRetentionDays);
