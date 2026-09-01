using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class RepoInviteExtensions
{
    public static Task<RepoInvite?> GetAsync(this DbSet<RepoInvite> dbSet, RepoInviteId id, CancellationToken cancellationToken)
    {
        return dbSet.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    /// <summary>
    /// The only lookup by code there is, and it is an equality on the canonical form - no prefix, no
    /// pattern, nothing a caller could walk.
    /// </summary>
    public static Task<RepoInvite?> GetByCodeAsync(this DbSet<RepoInvite> dbSet, InviteCode code, CancellationToken cancellationToken)
    {
        return dbSet.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
    }

    public static Task<List<RepoInvite>> GetForRepoAsync(this DbSet<RepoInvite> dbSet, RepoId repoId, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId)
            .OrderByDescending(x => x.Created)
            .ToListAsync(cancellationToken);
    }
}
