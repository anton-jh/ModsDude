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

    /// <summary>
    /// Whether a live repo already answers to this name. Archived ones are ignored - an archived
    /// repo gives up its name, which is what the filtered unique index enforces underneath.
    /// </summary>
    public static Task<bool> CheckNameIsTaken(this DbSet<Repo> dbSet, RepoName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.ArchivedAt == null && x.Name == name, cancellationToken);
    }

    /// <inheritdoc cref="CheckNameIsTaken(DbSet{Repo}, RepoName, CancellationToken)"/>
    public static Task<bool> CheckNameIsTaken(this DbSet<Repo> dbSet, RepoName name, RepoId except, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.ArchivedAt == null && x.Name == name && x.Id != except, cancellationToken);
    }

    public static async Task<Repo?> GetAsync(this DbSet<Repo> dbSet, RepoId repoId, CancellationToken cancellationToken)
    {
        return await dbSet.FindAsync(GetKey(repoId), cancellationToken);
    }
}
