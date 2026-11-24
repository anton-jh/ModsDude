using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Storage;

public interface IStorageMaintenance
{
    Task<StorageMaintenanceOutcome> RunAsync(StorageMaintenanceSettings settings, CancellationToken cancellationToken);

    Task<StorageInspection> InspectAsync(int minimumSoftDeleteDays, CancellationToken cancellationToken);

    /// <inheritdoc cref="IStorageGatekeeper.BindByHandAsync"/>
    Task<IReadOnlyList<string>> BindByHandAsync(string operatorName, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the file a deleted mod version left behind at once, so the version id can be uploaded
    /// again, as long as a run would be free to delete it now. Anything else keeps it for the run.
    /// </summary>
    /// <remarks>
    /// A run keeps a blob written within the minimum age, because a registration may have claimed it and
    /// not committed yet. This deletes regardless, so it holds the file's <see cref="BlobLocks"/> lock,
    /// which a registration holds from its claim until its commit: either the registration commits first
    /// and the file is referenced, or the file is gone before the registration claims it.
    /// </remarks>
    Task<ReleasedModFileDeletion> DeleteReleasedModFileAsync(RepoId repoId, ModId modId, ModVersionId versionId, int minimumSoftDeleteDays, CancellationToken cancellationToken);

    /// <summary>
    /// Queues the chosen unreferenced findings for deletion at the next run, each only while its blob is
    /// still the version the audit saw. Findings of any other kind are ignored.
    /// </summary>
    Task<QueuedDeletions> QueueForDeletionAsync(IReadOnlyCollection<long> findingIds, string operatorName, CancellationToken cancellationToken);
}
