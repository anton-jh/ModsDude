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
    /// Which of the named <c>(ModId, VersionId)</c> pairs the repo holds, read as keys only.
    /// </summary>
    /// <remarks>
    /// Two <c>IN</c> lists rather than one per pair, because a strongly-typed id has no tuple
    /// comparison the provider can translate. That over-selects - a version of a requested mod that
    /// happens to share a version id with another requested mod comes back too - so the caller
    /// matches the pairs itself. The surplus is bounded by the request.
    /// </remarks>
    public static async Task<HashSet<(ModId ModId, ModVersionId VersionId)>> GetExistingKeysAsync(
        this DbSet<ModVersion> dbSet,
        RepoId repoId,
        IReadOnlyCollection<ModId> modIds,
        IReadOnlyCollection<ModVersionId> versionIds,
        CancellationToken cancellationToken)
    {
        if (modIds.Count == 0)
        {
            return [];
        }

        var rows = await dbSet
            .Where(x => x.RepoId == repoId && modIds.Contains(x.ModId) && versionIds.Contains(x.Id))
            .Select(x => new { x.ModId, x.Id })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => (x.ModId, x.Id))];
    }

    public static async Task<Dictionary<RepoId, long>> GetRegisteredBytesPerRepoAsync(this DbSet<ModVersion> dbSet, CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .AsNoTracking()
            .GroupBy(x => x.RepoId)
            .Select(x => new { RepoId = x.Key, Bytes = x.Sum(y => y.SizeBytes) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.RepoId, x => x.Bytes);
    }

    public static async Task<Dictionary<RepoId, long>> GetScheduledForDeletionBytesPerRepoAsync(this DbSet<ModVersion> dbSet, CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .AsNoTracking()
            .Where(x => x.DeletionScheduledFor != null)
            .GroupBy(x => x.RepoId)
            .Select(x => new { RepoId = x.Key, Bytes = x.Sum(y => y.SizeBytes) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.RepoId, x => x.Bytes);
    }
}
