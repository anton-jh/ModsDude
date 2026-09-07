using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

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

    /// <summary>
    /// Whether a live profile already answers to this name. Archived ones are ignored, because an
    /// archived profile gives up its name - the filtered unique index says the same thing, and this
    /// is what turns the database's refusal into a message somebody can read.
    /// </summary>
    public static Task<bool> CheckNameIsTaken(this DbSet<Profile> dbSet, RepoId repoId, ProfileName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.ArchivedAt == null && x.Name == name, cancellationToken);
    }

    /// <inheritdoc cref="CheckNameIsTaken(DbSet{Profile}, RepoId, ProfileName, CancellationToken)"/>
    public static Task<bool> CheckNameIsTaken(this DbSet<Profile> dbSet, RepoId repoId, ProfileId except, ProfileName name, CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.ArchivedAt == null && x.Id != except && x.Name == name, cancellationToken);
    }

    /// <summary>
    /// Whether any savegame in the repo follows this profile, or any savegame version was played on
    /// one of its revisions. Either one makes the profile undeletable, and the delete endpoint
    /// refuses on it rather than letting the foreign keys behind it produce a database error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions because there are two facts, and they can disagree: <see cref="Savegame.ProfileId"/>
    /// is the standing intent that a save follows this profile, while
    /// <see cref="SavegameVersion.ProfileId"/> is what a version was actually played against. Move a
    /// save onto a branch and the old versions still honestly name the old profile - so asking only
    /// the first would let a profile somebody has played be deleted, and only the second would let
    /// one that has only ever been pointed at slip through. Both foreign keys are <c>Restrict</c>
    /// for that reason; see <c>SavegameEntityTypeConfiguration</c> and
    /// <c>SavegameVersionEntityTypeConfiguration</c>.
    /// </para>
    /// <para>
    /// History is what makes this strict, exactly as it is for a pinned mod version: a profile that
    /// has ever been played is a profile that cannot be deleted, because a version still names the
    /// revision it was played on. See
    /// <see cref="ProfileRevisionExtensions.CheckIfVersionIsDependedOn"/> for the same bargain one
    /// aggregate down.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The name and head revision of a handful of profiles at once, for a list that already knows
    /// which ids it is going to name.
    /// </summary>
    /// <remarks>
    /// Projected rather than materialized, like everything else that reads a profile: the entity
    /// itself is cheap, but nothing here needs to be tracked and a dictionary is what the caller
    /// wants anyway.
    /// </remarks>
    public static async Task<Dictionary<ProfileId, ProfileSummary>> GetSummariesAsync(
        this DbSet<Profile> dbSet,
        RepoId repoId, IReadOnlyCollection<ProfileId> profileIds,
        CancellationToken cancellationToken)
    {
        if (profileIds.Count == 0)
        {
            return [];
        }

        var rows = await dbSet
            .Where(x => x.RepoId == repoId && profileIds.Contains(x.Id))
            .Select(x => new ProfileSummary(x.Id, x.Name, x.HeadRevision))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id);
    }

    public static async Task<bool> CheckIfUsedBySavegameAsync(
        this DbSet<Savegame> dbSet,
        DbSet<SavegameVersion> versions,
        RepoId repoId, ProfileId profileId,
        CancellationToken cancellationToken)
    {
        return await dbSet.AnyAsync(x => x.RepoId == repoId && x.ProfileId == profileId, cancellationToken)
            || await versions.AnyAsync(x => x.RepoId == repoId && x.ProfileId == profileId, cancellationToken);
    }
}


/// <summary>A profile's row without its history: what a list naming several of them needs.</summary>
public record ProfileSummary(ProfileId Id, ProfileName Name, RevisionNumber HeadRevision);
