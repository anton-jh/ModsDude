using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class RepoExtensions
{
    public static object[] GetKey(this Repo repo)
    {
        return [repo.Id];
    }

    public static object[] GetKey(RepoId repoId)
    {
        return [repoId];
    }

    public static Task<bool> CheckNameIsTaken(this DbSet<Repo> dbSet, RepoName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.Name == name, cancellationToken);
    }

    public static Task<bool> CheckNameIsTaken(this DbSet<Repo> dbSet, RepoName name, RepoId except, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.Name == name && x.Id != except, cancellationToken);
    }

    public static async Task<Repo?> GetAsync(this DbSet<Repo> dbSet, RepoId repoId, CancellationToken cancellationToken)
    {
        return await dbSet.FindAsync(GetKey(repoId), cancellationToken);
    }
}
