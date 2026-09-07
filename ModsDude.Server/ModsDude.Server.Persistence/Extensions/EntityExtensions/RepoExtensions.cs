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

    // No CheckNameIsTaken here, unlike profiles and savegames: a repo name is never taken. Those two
    // are unique within their repo because a profile is picked by name out of a list the whole group
    // shares; a repo is only ever arrived at through an invite code.

    public static async Task<Repo?> GetAsync(this DbSet<Repo> dbSet, RepoId repoId, CancellationToken cancellationToken)
    {
        return await dbSet.FindAsync(GetKey(repoId), cancellationToken);
    }
}
