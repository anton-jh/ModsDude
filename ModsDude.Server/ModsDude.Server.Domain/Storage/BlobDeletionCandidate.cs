using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// A blob the database has said may be garbage. The only way a blob is ever deleted: nothing deletes a
/// blob because the database fails to mention it.
/// </summary>
/// <remarks>
/// <para>
/// Queued by the database itself: its triggers queue a blob whenever a row that refers to one is
/// deleted or stops referring to it, and the upload endpoints queue the blob they hand out a link for,
/// so an upload that is never registered is collected too. Admin deletions are queued by the admin page.
/// </para>
/// <para>
/// One row per blob. Queuing a blob again moves <see cref="QueuedAt"/> to the latest reason and counts
/// <see cref="Requeued"/>, so a deletion run that read the row before it was queued again never drops it.
/// </para>
/// </remarks>
public class BlobDeletionCandidate
{
    private BlobDeletionCandidate()
    {
    }

    /// <summary>A candidate as the database's queue function writes it. Nothing in the server constructs one otherwise.</summary>
    public BlobDeletionCandidate(
        StorageContainer container,
        string blobName,
        BlobDeletionReason reason,
        RepoId? repoId = null,
        UserId? userId = null,
        string? requestedBy = null,
        string? expectedVersion = null,
        DateTime queuedAt = default)
    {
        Container = container;
        BlobName = blobName;
        Reason = reason;
        RepoId = repoId;
        UserId = userId;
        RequestedBy = requestedBy;
        ExpectedVersion = expectedVersion;
        QueuedAt = queuedAt;
    }


    public long Id { get; private set; }
    public StorageContainer Container { get; private set; }
    public string BlobName { get; private set; } = string.Empty;
    public BlobDeletionReason Reason { get; private set; }

    /// <summary>The repo the blob was queued in, where the reason has one.</summary>
    public RepoId? RepoId { get; private set; }

    /// <summary>Who was handed the upload link, or whose picture the blob was.</summary>
    public UserId? UserId { get; private set; }

    /// <summary>The admin who asked, for <see cref="BlobDeletionReason.Admin"/>.</summary>
    public string? RequestedBy { get; private set; }

    /// <summary>The version the admin saw, for <see cref="BlobDeletionReason.Admin"/>. A blob changed since is kept.</summary>
    public string? ExpectedVersion { get; private set; }

    /// <summary>When the database last queued the blob, by its own clock.</summary>
    public DateTime QueuedAt { get; private set; }

    public int Requeued { get; private set; }
}


public enum BlobDeletionReason
{
    /// <summary>A row that referred to the blob was deleted or changed.</summary>
    ReferenceRemoved,

    /// <summary>An upload link was handed out for the blob, or the API stored it, before anything referred to it.</summary>
    UploadIssued,

    /// <summary>An admin chose it on the admin page.</summary>
    Admin
}
