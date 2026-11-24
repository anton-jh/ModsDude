using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Persistence.Storage;

public interface IBlobDeleter
{
    /// <returns>How many blobs were deleted.</returns>
    Task<int> DeleteQueuedAsync(StorageMaintenanceSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the candidate's blob now, however recently it was written, as long as nothing refers to it. Safe only while nothing can claim the blob; see
    /// <see cref="IStorageMaintenance.DeleteReleasedModFileAsync"/>.
    /// </summary>
    /// <returns>Whether it was deleted.</returns>
    Task<bool> DeleteNowAsync(BlobDeletionCandidate candidate, CancellationToken cancellationToken);
}
