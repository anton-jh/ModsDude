using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class ProfileExtensions
{
    public static object[] GetKey(this Profile profile)
    {
        return [profile.RepoId, profile.Id];
    }

    public static object[] GetKey(RepoId repoId, ProfileId profileId)
    {
        return [repoId, profileId];
    }

    public static ValueTask<Profile?> GetAsync(this DbSet<Profile> dbSet, RepoId repoId, ProfileId profileId, CancellationToken cancellationToken)
    {
        return dbSet.FindAsync(GetKey(repoId, profileId), cancellationToken);
    }

    /// <summary>
    /// Loads a profile with each dependency's <see cref="ModDependency.ModVersion"/> populated.
    /// Every domain operation on a dependency reads <c>(RepoId, ModId)</c> off it —
    /// <see cref="Profile.AddDependency"/>, <see cref="Profile.DeleteDependency(ModId)"/>,
    /// <see cref="Profile.HasDependencyOn"/>, <see cref="ModDependency.ChangeVersion"/> — so loading
    /// the profile without it makes every one of those throw. The navigation is one hop now that
    /// <see cref="ModVersion"/> has no parent, but it is still not auto-included.
    /// <see cref="GetAsync"/> is fine for anything that only touches the profile itself.
    /// </summary>
    public static Task<Profile?> GetWithModDependenciesAsync(this DbSet<Profile> dbSet, RepoId repoId, ProfileId profileId, CancellationToken cancellationToken)
    {
        return dbSet
            .Include(x => x.ModDependencies)
                .ThenInclude(x => x.ModVersion)
            .FirstOrDefaultAsync(x => x.RepoId == repoId && x.Id == profileId, cancellationToken);
    }

    /// <summary>
    /// Whether any profile in the repo pins this exact version. Deleting one that is pinned would
    /// silently drop a mod out of somebody else's profile, so the delete endpoints refuse instead —
    /// and the <see cref="ModDependency"/> foreign key is Restrict so the database refuses too,
    /// rather than cascading the row away behind the check.
    /// </summary>
    public static Task<bool> CheckIfVersionIsDependedOn(this DbSet<Profile> dbSet, RepoId repoId, ModId modId, ModVersionId modVersionId, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId)
            .SelectMany(x => x.ModDependencies)
            .AnyAsync(x => x.ModVersion.ModId == modId && x.ModVersion.Id == modVersionId, cancellationToken);
    }

    /// <inheritdoc cref="CheckIfVersionIsDependedOn"/>
    public static Task<bool> CheckIfModIsDependedOn(this DbSet<Profile> dbSet, RepoId repoId, ModId modId, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId)
            .SelectMany(x => x.ModDependencies)
            .AnyAsync(x => x.ModVersion.ModId == modId, cancellationToken);
    }

    /// <summary>
    /// How many of the repo's profiles pin each version, for the versions at least one profile pins.
    /// Ordered by <c>(ModId, VersionId)</c> and windowed, because a repo's dependency rows are its
    /// profile count times its profile sizes — thousands of mods each — and nothing that renders a
    /// catalog may issue a query without a bound.
    /// </summary>
    /// <remarks>
    /// Sparse on purpose. The rows that are missing are the answer "no profile pins this", so the
    /// result is proportional to what is actually used rather than to the size of the catalog, and
    /// it collapses every profile's copy of a dependency into one row per version.
    /// </remarks>
    public static Task<List<ModVersionUsage>> GetModUsageAsync(this DbSet<Profile> dbSet, RepoId repoId, int skip, int take, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId)
            .SelectMany(x => x.ModDependencies)
            .GroupBy(x => new { x.ModVersion.ModId, VersionId = x.ModVersion.Id })
            // A strongly-typed id has no comparison the provider can translate, so the window is an
            // offset rather than the keyset tuple the ordering would otherwise allow.
            .OrderBy(x => x.Key.ModId).ThenBy(x => x.Key.VersionId)
            .Select(x => new ModVersionUsage(x.Key.ModId, x.Key.VersionId, x.Count()))
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public static Task<bool> CheckNameIsTaken(this DbSet<Profile> dbSet, RepoId repoId, ProfileName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.Name == name, cancellationToken);
    }

    public static Task<bool> CheckNameIsTaken(this DbSet<Profile> dbSet, RepoId repoId, ProfileId except, ProfileName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.Id != except && x.Name == name, cancellationToken);
    }

    public static async Task<bool> CheckNameIsTaken(this DbSet<Profile> dbSet, RepoId repoId, ProfileName name, ProfileId except, CancellationToken cancellationToken)
    {
        var profile = await dbSet
            .FindAsync(GetKey(repoId, except), cancellationToken)
            ?? throw new ArgumentException("No profile with provided id exists", nameof(except));

        return await dbSet
            .Where(x => x.RepoId == profile.RepoId)
            .AnyAsync(x => x.Name == name && x.Id != except, cancellationToken);
    }
}


/// <summary>
/// One registered version and how many of its repo's profiles pin it. A count rather than the
/// profiles themselves: the row is read for a whole catalog at once, and what the Manage page needs
/// of it is whether the number is zero.
/// </summary>
public record ModVersionUsage(ModId ModId, ModVersionId VersionId, int ProfileCount);
