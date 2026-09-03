using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// The profile row itself - name, timestamps and which revision is its head. What it pins is not
    /// here and cannot be reached from here: see <see cref="ProfileRevisionExtensions"/>, and
    /// <see cref="Profile"/> for why the navigation does not exist.
    /// </summary>
    public static ValueTask<Profile?> GetAsync(this DbSet<Profile> dbSet, RepoId repoId, ProfileId profileId, CancellationToken cancellationToken)
    {
        return dbSet.FindAsync(GetKey(repoId, profileId), cancellationToken);
    }

    public static Task<bool> CheckNameIsTaken(this DbSet<Profile> dbSet, RepoId repoId, ProfileName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.Name == name, cancellationToken);
    }

    public static Task<bool> CheckNameIsTaken(this DbSet<Profile> dbSet, RepoId repoId, ProfileId except, ProfileName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.Id != except && x.Name == name, cancellationToken);
    }
}
