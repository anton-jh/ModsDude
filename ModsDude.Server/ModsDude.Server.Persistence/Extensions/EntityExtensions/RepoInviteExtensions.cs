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

    /// <summary>
    /// The repo's invite list: everything that has not been taken off it.
    /// </summary>
    /// <remarks>
    /// Dismissed invites are filtered here rather than at the endpoint, so nothing can accidentally
    /// serve one. Revoking dismisses, so a retired code disappears the moment it is switched off; an
    /// expired or exhausted one stays until somebody removes it, because its absence would read as
    /// "no invite was ever made" rather than "it ran out". See <see cref="RepoInvite.DismissedAt"/>.
    /// </remarks>
    public static Task<List<RepoInvite>> GetForRepoAsync(this DbSet<RepoInvite> dbSet, RepoId repoId, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId && x.DismissedAt == null)
            .OrderByDescending(x => x.Created)
            .ToListAsync(cancellationToken);
    }
}
