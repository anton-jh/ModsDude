using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

public static class RepoMembershipExtensions
{
    public static object[] GetKey(this RepoMembership repoMembership)
    {
        return [repoMembership.UserId, repoMembership.RepoId];
    }

    public static async ValueTask<IEnumerable<RepoMembership>> GetByRepoIdAsync(this DbSet<RepoMembership> dbSet, RepoId repoId, CancellationToken cancellationToken = default)
    {
        return await dbSet
            .Where(m => m.RepoId == repoId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Everybody's level in the repo, by who they are.</summary>
    public static Task<Dictionary<UserId, RepoMembershipLevel>> GetLevelsAsync(this DbSet<RepoMembership> dbSet, RepoId repoId, CancellationToken cancellationToken)
    {
        return dbSet
            .AsNoTracking()
            .Where(x => x.RepoId == repoId)
            .ToDictionaryAsync(x => x.UserId, x => x.Level, cancellationToken);
    }

    public static async ValueTask<IEnumerable<RepoMembership>> GetByUserIdAsync(this DbSet<RepoMembership> dbSet, UserId userId, CancellationToken cancellationToken = default)
    {
        return await dbSet
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);
    }
}
