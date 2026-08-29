using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class ModVersionExtensions
{
    public static object[] GetKey(this ModVersion modVersion)
    {
        return [modVersion.RepoId, modVersion.ModId, modVersion.Id];
    }


    public static object[] GetKey(RepoId repoId, ModId modId, ModVersionId modVersionId)
    {
        return [repoId, modId, modVersionId];
    }

    public static ValueTask<ModVersion?> GetAsync(this DbSet<ModVersion> dbSet, RepoId repoId, ModId modId, ModVersionId modVersionId, CancellationToken cancellationToken)
    {
        return dbSet.FindAsync(GetKey(repoId, modId, modVersionId), cancellationToken);
    }

    /// <summary>
    /// Every version sharing one <c>(RepoId, ModId)</c>, tracked. This is the sibling set the
    /// domain needs wherever it used to reach through a parent — placement, upgrades, and closing
    /// the gap after a removal.
    /// </summary>
    public static Task<List<ModVersion>> GetVersionsOfModAsync(this DbSet<ModVersion> dbSet, RepoId repoId, ModId modId, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId && x.ModId == modId)
            .ToListAsync(cancellationToken);
    }
}
