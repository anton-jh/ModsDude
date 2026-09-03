using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

/// <summary>
/// Everything that reads a profile's mod list.
/// </summary>
/// <remarks>
/// All of it projects rather than materializes. A <see cref="ProfileRevision"/> entity carries its
/// dependencies as an owned collection, which EF always loads with it - so loading one revision of a
/// two-thousand-mod profile reads two thousand rows whether or not anything wanted them, and
/// loading a history reads that again per revision. Nothing here loads a revision entity; the only
/// one that is ever materialized is a new one, on its way in.
/// </remarks>
public static class ProfileRevisionExtensions
{
    /// <summary>
    /// What a revision pins, in the lightweight form a comparison works in.
    /// </summary>
    public static async Task<List<ProfileModPin>> GetPinsAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ProfileId profileId, RevisionNumber number,
        CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId && x.Number == number)
            .SelectMany(x => x.ModDependencies)
            .Select(x => new { x.ModVersion.ModId, VersionId = x.ModVersion.Id, x.Locked })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new ProfileModPin(x.ModId, x.VersionId, x.Locked))];
    }

    /// <summary>
    /// The same set with each version's content hash, which is what sync reads: it works from a
    /// profile's dependencies rather than from the repo's mod list, and without the hash here every
    /// sync would have to pull the unpaged mod list to resolve it.
    /// </summary>
    public static async Task<List<ProfileModDependencyRow>> GetDependencyRowsAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ProfileId profileId, RevisionNumber number,
        CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId && x.Number == number)
            .SelectMany(x => x.ModDependencies)
            .Select(x => new
            {
                x.ModVersion.ModId,
                VersionId = x.ModVersion.Id,
                x.ModVersion.ContentHash,
                x.Locked
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new ProfileModDependencyRow(x.ModId, x.VersionId, x.ContentHash, x.Locked))];
    }

    public static Task<bool> ExistsAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ProfileId profileId, RevisionNumber number,
        CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.ProfileId == profileId && x.Number == number, cancellationToken);
    }

    /// <summary>
    /// Whether any revision of any profile in the repo pins this exact version. Deleting one that is
    /// pinned would silently drop a mod out of somebody's profile - or rewrite what an old revision
    /// says was installed - so the delete endpoints refuse instead, and the
    /// <see cref="ModDependency"/> foreign key is Restrict so the database refuses too.
    /// </summary>
    /// <remarks>
    /// History is what makes this strict. A version any profile has <em>ever</em> pinned stays
    /// pinned by the revision that pinned it, so in practice a version that has been used is a
    /// version that cannot be deleted. That is the price of an old revision still meaning something:
    /// see docs/02-domain-model.md#a-pinned-version-cannot-be-deleted-any-more.
    /// </remarks>
    public static Task<bool> CheckIfVersionIsDependedOn(this DbSet<ProfileRevision> dbSet, RepoId repoId, ModId modId, ModVersionId modVersionId, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId)
            .SelectMany(x => x.ModDependencies)
            .AnyAsync(x => x.ModVersion.ModId == modId && x.ModVersion.Id == modVersionId, cancellationToken);
    }

    /// <inheritdoc cref="CheckIfVersionIsDependedOn"/>
    public static Task<bool> CheckIfModIsDependedOn(this DbSet<ProfileRevision> dbSet, RepoId repoId, ModId modId, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId)
            .SelectMany(x => x.ModDependencies)
            .AnyAsync(x => x.ModVersion.ModId == modId, cancellationToken);
    }

    /// <summary>
    /// How many of the repo's profiles pin each version, for the versions at least one of them pins.
    /// Ordered by <c>(ModId, VersionId)</c> and windowed, because a repo's dependency rows are its
    /// profile count times its revision count times its profile sizes - thousands of mods each - and
    /// nothing that renders a catalog may issue a query without a bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sparse on purpose. The rows that are missing are the answer "no profile pins this", so the
    /// result is proportional to what is actually used rather than to the size of the catalog.
    /// </para>
    /// <para>
    /// A profile that pinned a version in ten revisions counts once, which is what the
    /// <c>Distinct</c> is for - but it does count, whether or not its <em>current</em> revision
    /// still pins it. The number exists to tell somebody whether a delete will be refused, and a
    /// version any revision holds is a version the foreign key will not let go of.
    /// </para>
    /// </remarks>
    public static Task<List<ModVersionUsage>> GetModUsageAsync(this DbSet<ProfileRevision> dbSet, RepoId repoId, int skip, int take, CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId)
            .SelectMany(
                x => x.ModDependencies,
                (revision, dependency) => new
                {
                    revision.ProfileId,
                    dependency.ModVersion.ModId,
                    VersionId = dependency.ModVersion.Id
                })
            .Distinct()
            .GroupBy(x => new { x.ModId, x.VersionId })
            // A strongly-typed id has no comparison the provider can translate, so the window is an
            // offset rather than the keyset tuple the ordering would otherwise allow.
            .OrderBy(x => x.Key.ModId).ThenBy(x => x.Key.VersionId)
            .Select(x => new ModVersionUsage(x.Key.ModId, x.Key.VersionId, x.Count()))
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}


/// <summary>One mod as a revision pins it, with the content hash sync needs to fetch the file.</summary>
public record ProfileModDependencyRow(ModId ModId, ModVersionId VersionId, string ContentHash, bool Locked)
{
    public ProfileModPin ToPin() => new(ModId, VersionId, Locked);
}


/// <summary>
/// One registered version and how many of its repo's profiles pin it. A count rather than the
/// profiles themselves: the row is read for a whole catalog at once, and what the Manage page needs
/// of it is whether the number is zero.
/// </summary>
public record ModVersionUsage(ModId ModId, ModVersionId VersionId, int ProfileCount);
