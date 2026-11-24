namespace ModsDude.Server.Domain.Storage;

public enum BlobDeletionVerdict
{
    /// <summary>Delete it, conditional on <see cref="StoredBlob.Version"/>.</summary>
    Delete,

    /// <summary>Nothing to delete: forget the candidate.</summary>
    Drop,

    /// <summary>Not now: keep the candidate for a later run.</summary>
    Keep
}

public sealed record BlobDeletionDecision(BlobDeletionVerdict Verdict, string Reason);

/// <summary>
/// What becomes of one due candidate. Pure, and the last word before a blob is deleted.
/// </summary>
/// <remarks>
/// <para>
/// The caller reads the blob before it asks the database whether anything refers to it, and deletes
/// only on the version it read. Whatever is about to refer to a blob claims it first, and the claim
/// writes to the blob: claimed before the read, the blob is too recent to judge; claimed after it, the
/// version has moved and the delete fails. Either way a blob that is about to be referred to stays.
/// </para>
/// <para>
/// An upload's candidate is queued before its bytes exist, so a blob that is not there yet stays queued
/// until the upload can no longer be under way.
/// </para>
/// </remarks>
public static class BlobDeletionRule
{
    /// <param name="stored">The blob as read just now, or null where there is none.</param>
    /// <param name="isReferenced">Whether anything refers to the blob, asked after <paramref name="stored"/> was read.</param>
    /// <param name="isRequeued">Whether the candidate was queued again since it was read, asked after <paramref name="isReferenced"/>.</param>
    public static BlobDeletionDecision Decide(
        BlobDeletionCandidate candidate,
        StoredBlob? stored,
        bool isReferenced,
        bool isRequeued,
        DateTime now,
        TimeSpan minimumAge)
    {
        if (!BlobNames.IsRecognised(candidate.Container, candidate.BlobName))
        {
            return new(BlobDeletionVerdict.Keep, "its name does not parse");
        }

        if (stored is null)
        {
            return candidate.Reason == BlobDeletionReason.UploadIssued && now - candidate.QueuedAt < minimumAge
                ? new(BlobDeletionVerdict.Keep, "its upload may still be under way")
                : new(BlobDeletionVerdict.Drop, "it is gone");
        }

        if (stored.Version is null)
        {
            return new(BlobDeletionVerdict.Keep, "storage gave no version to delete it on");
        }

        if (now - stored.LastModified.UtcDateTime < minimumAge)
        {
            return new(BlobDeletionVerdict.Keep, "it was written too recently");
        }

        if (isReferenced)
        {
            return new(BlobDeletionVerdict.Drop, "something refers to it");
        }

        if (candidate.ExpectedVersion is string expected && expected != stored.Version)
        {
            return new(BlobDeletionVerdict.Drop, "it changed after it was chosen");
        }

        if (isRequeued)
        {
            return new(BlobDeletionVerdict.Keep, "it was queued again while this run looked at it");
        }

        return new(BlobDeletionVerdict.Delete, "nothing refers to it");
    }
}
