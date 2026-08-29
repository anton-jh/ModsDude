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
