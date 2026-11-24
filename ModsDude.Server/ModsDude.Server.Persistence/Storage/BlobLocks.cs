using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// A session advisory lock on one blob, which a registration holds from claiming its file until it
/// commits, and a deletion that skips the minimum age holds while it deletes. See
/// <see cref="IStorageMaintenance.DeleteReleasedModFileAsync"/>.
/// </summary>
public static class BlobLocks
{
    /// <summary>Waits for the lock. Disposing the result releases it.</summary>
    public static async Task<IAsyncDisposable> LockBlobAsync(
        this ApplicationDbContext dbContext,
        StorageContainer container,
        string blobName,
        CancellationToken cancellationToken)
    {
        var key = $"{container}/{blobName}";

        // Session level, so the connection stays open while the lock is held.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        var locked = false;

        try
        {
            await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({AdvisoryLockSpaces.Blob}, hashtext({key}))", cancellationToken);
            locked = true;
        }
        finally
        {
            if (!locked)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }

        return new HeldLock(dbContext, key);
    }


    private sealed class HeldLock(ApplicationDbContext dbContext, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({AdvisoryLockSpaces.Blob}, hashtext({key}))", CancellationToken.None);
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
