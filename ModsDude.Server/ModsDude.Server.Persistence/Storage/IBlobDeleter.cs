using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Persistence.Storage;

public interface IBlobDeleter
{
    /// <summary>
    /// Deletes the candidate's blob where <see cref="BlobDeletionRule"/> clears it, records the delete,
    /// and takes the candidate off the queue where the rule says it is done with.
    /// </summary>
    /// <param name="minimumAge">
    /// How long the blob must have sat untouched. Zero is safe only while nothing can claim the blob;
    /// see <see cref="IStorageMaintenance.DeleteReleasedModFileAsync"/>.
    /// </param>
    Task<BlobDeletionResult> DeleteAsync(BlobDeletionCandidate candidate, TimeSpan minimumAge, CancellationToken cancellationToken);
}


public enum BlobDeletionResult
{
    /// <summary>Deleted and recorded.</summary>
    Deleted,

    /// <summary>There was no blob to delete. Off the queue.</summary>
    Gone,

    /// <summary>Something refers to the blob, or it changed after an admin chose it. Off the queue, and kept.</summary>
    Dropped,

    /// <summary>Kept, and still queued for a later run.</summary>
    Kept
}
