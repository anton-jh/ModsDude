using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// Queues blobs for the deletion run through <c>queue_blob_deletion</c>, the database function the
/// delete triggers call too, so a blob is queued the same way whoever queues it. See
/// <see cref="BlobDeletionCandidate"/>.
/// </summary>
public static class BlobDeletionQueue
{
    public const string UniqueIndex = "IX_BlobDeletionCandidates_Container_BlobName";


    /// <summary>
    /// Queues a blob an upload link is about to be handed out for, or that the API is about to store.
    /// Called before either, so no blob is written that nothing will ever collect.
    /// </summary>
    public static async Task QueueUploadAsync(
        this ApplicationDbContext dbContext,
        StorageContainer container,
        string blobName,
        RepoId? repoId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var containerName = container.ToString();
        var reason = BlobDeletionReason.UploadIssued.ToString();
        var repo = repoId?.Value;

        await dbContext.Database.ExecuteSqlAsync(
            $"SELECT queue_blob_deletion({containerName}, {blobName}, {reason}, {repo}, {userId.Value}, NULL, NULL)",
            cancellationToken);
    }

    /// <summary>Queues a blob an admin chose, to be deleted only while it is still at <paramref name="expectedVersion"/>.</summary>
    public static async Task QueueAdminAsync(
        this ApplicationDbContext dbContext,
        StorageContainer container,
        string blobName,
        string expectedVersion,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var containerName = container.ToString();
        var reason = BlobDeletionReason.Admin.ToString();
        var repo = BlobNames.OwningRepo(container, blobName)?.Value;

        await dbContext.Database.ExecuteSqlAsync(
            $"SELECT queue_blob_deletion({containerName}, {blobName}, {reason}, {repo}, NULL, {requestedBy}, {expectedVersion})",
            cancellationToken);
    }
}
