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

    /// <summary>
    /// The newest version of each of the named mods, tracked, so that a dependency can be pointed at
    /// one. What a batch upgrade needs: it reads one row per mod rather than the whole sibling set of
    /// each, which for a profile holding two thousand mods is the difference between a query and a
    /// materialization of the repo.
    /// </summary>
    /// <remarks>
    /// Expressed as "no sibling sits after it" rather than as a grouped maximum, because a grouped
    /// maximum projects and a dependency can only be moved to an entity.
    /// </remarks>
    public static async Task<Dictionary<ModId, ModVersion>> GetLatestVersionOfEachAsync(
        this DbSet<ModVersion> dbSet,
        RepoId repoId,
        IReadOnlyCollection<ModId> modIds,
        CancellationToken cancellationToken)
    {
        if (modIds.Count == 0)
        {
            return [];
        }

        var latest = await dbSet
            .Where(x => x.RepoId == repoId && modIds.Contains(x.ModId))
            .Where(x => !dbSet.Any(y => y.RepoId == x.RepoId && y.ModId == x.ModId && y.SequenceNumber > x.SequenceNumber))
            .ToListAsync(cancellationToken);

        return latest.ToDictionary(x => x.ModId);
    }
}
