using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.ModHub;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

public static class ModHubModExtensions
{
    /// <summary>Every stored mod of <paramref name="game"/> whose file name key is one of <paramref name="keys"/>.</summary>
    public static Task<List<ModHubMod>> GetByFileNameKeysAsync(
        this DbSet<ModHubMod> dbSet,
        string game, IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        return dbSet
            .AsNoTracking()
            .Where(x => x.Game == game && keys.Contains(x.FileNameKey))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Which of <paramref name="modHubIds"/> are already stored for <paramref name="game"/>.</summary>
    public static async Task<HashSet<int>> GetKnownIdsAsync(
        this DbSet<ModHubMod> dbSet,
        string game, IReadOnlyCollection<int> modHubIds,
        CancellationToken cancellationToken)
    {
        var known = await dbSet
            .Where(x => x.Game == game && modHubIds.Contains(x.ModHubId))
            .Select(x => x.ModHubId)
            .ToListAsync(cancellationToken);

        return [.. known];
    }

    /// <summary>The <paramref name="count"/> mods of <paramref name="game"/> read longest ago.</summary>
    public static Task<List<int>> GetStalestIdsAsync(
        this DbSet<ModHubMod> dbSet,
        string game, int count,
        CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.Game == game)
            .OrderBy(x => x.FetchedAt)
            .Take(count)
            .Select(x => x.ModHubId)
            .ToListAsync(cancellationToken);
    }
}
