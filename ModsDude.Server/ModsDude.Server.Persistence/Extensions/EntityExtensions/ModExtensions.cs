using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class ModExtensions
{
    public static object[] GetKey(this Mod mod)
    {
        return [mod.RepoId, mod.Id];
    }


    public static object[] GetKey(RepoId repoId, ModId modId)
    {
        return [repoId, modId];
    }

    public static ValueTask<Mod?> GetAsync(this DbSet<Mod> dbSet, RepoId repoId, ModId modId, CancellationToken cancellationToken)
    {
        return dbSet.FindAsync(GetKey(repoId, modId), cancellationToken);
    }
}
