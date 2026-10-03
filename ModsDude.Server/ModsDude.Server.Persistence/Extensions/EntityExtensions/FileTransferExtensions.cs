using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class FileTransferExtensions
{
    /// <summary>
    /// The transfers since <paramref name="since"/>, totalled per repo, user, file and direction.
    /// </summary>
    public static async Task<List<FileTransferTotal>> GetTotalsSinceAsync(
        this DbSet<FileTransfer> dbSet,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .AsNoTracking()
            .Where(x => x.At >= since)
            .GroupBy(x => new { x.RepoId, x.UserId, x.File, x.Direction })
            .Select(x => new
            {
                x.Key.RepoId,
                x.Key.UserId,
                x.Key.File,
                x.Key.Direction,
                Count = x.Count(),
                Bytes = x.Sum(y => y.SizeBytes)
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new FileTransferTotal(x.RepoId, x.UserId, x.File, x.Direction, x.Count, x.Bytes))];
    }
}


public record FileTransferTotal(
    RepoId RepoId,
    UserId UserId,
    TransferredFile File,
    TransferDirection Direction,
    int Count,
    long Bytes);
