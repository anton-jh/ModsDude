using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Invites;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class TrustCodeExtensions
{
    public static Task<TrustCode?> GetAsync(this DbSet<TrustCode> dbSet, TrustCodeId id, CancellationToken cancellationToken)
    {
        return dbSet.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public static Task<TrustCode?> GetByCodeAsync(this DbSet<TrustCode> dbSet, InviteCode code, CancellationToken cancellationToken)
    {
        return dbSet.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
    }

    public static Task<TrustCode?> GetByRequestIdAsync(this DbSet<TrustCode> dbSet, TrustCodeRequestId requestId, CancellationToken cancellationToken)
    {
        return dbSet.FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
    }

    public static Task<List<TrustCode>> GetLatestAsync(this DbSet<TrustCode> dbSet, int count, CancellationToken cancellationToken)
    {
        return dbSet
            .OrderByDescending(x => x.Created)
            .ThenBy(x => x.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}
