using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;

/// <summary>
/// Everything that reads a profile's history, or the mod list of one revision of it.
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
                x.ModVersion.FileName,
                x.ModVersion.ContentHash,
                x.Locked
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new ProfileModDependencyRow(x.ModId, x.VersionId, x.FileName, x.ContentHash, x.Locked))];
    }

    public static Task<bool> ExistsAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ProfileId profileId, RevisionNumber number,
        CancellationToken cancellationToken)
    {
        return dbSet.AnyAsync(x => x.RepoId == repoId && x.ProfileId == profileId && x.Number == number, cancellationToken);
    }

    /// <summary>
    /// A profile's history without its mod lists - newest first, windowed by offset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ordered and windowed before it is projected</b>, and that order matters. A provider cannot
    /// see through a constructor-bound record: ordering by a member of the projection asks it to map
    /// <c>new ProfileRevisionRow(...).Number</c> back to a column, which it refuses outright. Sorting
    /// the entities and projecting the page is the same query and translates.
    /// </para>
    /// <para>
    /// An offset rather than a keyset, like the mod usage listing: <see cref="RevisionNumber"/> is a
    /// value object and a provider cannot translate a comparison on one. Ordering by it is fine -
    /// that is the stored column - it is <c>&lt;</c> and <c>&gt;</c> that have nowhere to go. New
    /// revisions arrive at the front, so a page read while somebody is saving can repeat a row.
    /// </para>
    /// </remarks>
    public static Task<List<ProfileRevisionRow>> GetHistoryAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ProfileId profileId,
        int skip, int take,
        CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId)
            .OrderByDescending(x => x.Number)
            .Skip(skip)
            .Take(take)
            .Select(x => new ProfileRevisionRow(
                x.Number,
                x.Created,
                x.CreatedBy,
                x.Label,
                x.Origin,
                x.SourceProfileId,
                x.SourceRevision,
                x.ModCount,
                x.Changes.Added,
                x.Changes.Changed,
                x.Changes.Removed))
            .ToListAsync(cancellationToken);
    }

    /// <summary>One revision's entry, or <c>null</c> where the profile has no such revision.</summary>
    public static Task<ProfileRevisionRow?> GetRowAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ProfileId profileId, RevisionNumber number,
        CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId && x.Number == number)
            .Select(x => new ProfileRevisionRow(
                x.Number,
                x.Created,
                x.CreatedBy,
                x.Label,
                x.Origin,
                x.SourceProfileId,
                x.SourceRevision,
                x.ModCount,
                x.Changes.Added,
                x.Changes.Changed,
                x.Changes.Removed))
            .FirstOrDefaultAsync(cancellationToken);
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
    /// Drops revisions of one profile, rows and all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Leaves a gap, and that is the point.</b> Revision numbers exist to be said out loud;
    /// renumbering would make yesterday's sentence point at a different list. Savegame version
    /// numbers already work this way, and for the same reason.
    /// </para>
    /// <para>
    /// <c>ExecuteDelete</c> rather than loading the entities: a revision's dependencies are an owned
    /// collection, so materializing fifty of a two-thousand-mod profile's revisions to delete them
    /// would read a hundred thousand rows first. The owned rows go with the principal in the
    /// database, which is where the cascade belongs.
    /// </para>
    /// </remarks>
    public static Task<int> DeleteRevisionsAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ProfileId profileId,
        IReadOnlyCollection<RevisionNumber> numbers,
        CancellationToken cancellationToken)
    {
        if (numbers.Count == 0)
        {
            return Task.FromResult(0);
        }

        return dbSet
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId && numbers.Contains(x.Number))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Which of the named revisions the profile actually has.</summary>
    public static async Task<HashSet<RevisionNumber>> GetExistingAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ProfileId profileId, IReadOnlyCollection<RevisionNumber> numbers,
        CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .Where(x => x.RepoId == repoId && x.ProfileId == profileId && numbers.Contains(x.Number))
            .Select(x => x.Number)
            .ToListAsync(cancellationToken);

        return [.. rows];
    }

    /// <summary>
    /// Which revisions of which profiles pin a mod, so a refused delete can say what is holding it
    /// rather than only that something is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every revision, not only the head ones.</b> That is what the foreign key enforces and
    /// therefore what the user has to act on: a version pinned once, five hundred revisions ago, is
    /// as undeletable as one pinned now. Saying "profile X" without saying which revisions would
    /// send somebody to a history page with nothing to look for.
    /// </para>
    /// <para>
    /// Bounded, because a repo's dependency rows are its profile count times its revision count
    /// times its profile sizes. The caller is drawing a list somebody reads, so the cap is generous
    /// enough to be the whole answer in every ordinary case and the response says when it was not.
    /// </para>
    /// </remarks>
    /// <param name="modVersionId">
    /// One version, or null for the whole mod - the two things that can be deleted, and the same
    /// query either way.
    /// </param>
    public static async Task<List<ProfileRevisionDependency>> GetDependentRevisionsAsync(
        this DbSet<ProfileRevision> dbSet,
        RepoId repoId, ModId modId, ModVersionId? modVersionId,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await dbSet
            .Where(x => x.RepoId == repoId)
            .SelectMany(
                x => x.ModDependencies,
                (revision, dependency) => new
                {
                    revision.ProfileId,
                    revision.Number,
                    dependency.ModVersion.ModId,
                    VersionId = dependency.ModVersion.Id
                })
            .Where(x => x.ModId == modId && (modVersionId == null || x.VersionId == modVersionId))
            .OrderBy(x => x.ProfileId).ThenBy(x => x.Number)
            .Take(take)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new ProfileRevisionDependency(x.ProfileId, x.Number, x.VersionId))];
    }

    /// <summary>
    /// Which savegame versions were played on a profile revision. The other half of what stops a
    /// revision being deleted, and the only one the user can do anything about.
    /// </summary>
    public static Task<List<SavegameRevisionDependency>> GetDependentSavegameVersionsAsync(
        this DbSet<Domain.Savegames.SavegameVersion> dbSet,
        RepoId repoId, ProfileId profileId, IReadOnlyCollection<RevisionNumber> revisions,
        CancellationToken cancellationToken)
    {
        return dbSet
            .Where(x => x.RepoId == repoId
                && x.ProfileId == profileId
                && revisions.Contains(x.ProfileRevision))
            .OrderBy(x => x.SavegameId).ThenBy(x => x.Number)
            .Select(x => new SavegameRevisionDependency(x.SavegameId, x.Number, x.ProfileRevision))
            .ToListAsync(cancellationToken);
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
public record ProfileModDependencyRow(ModId ModId, ModVersionId VersionId, string FileName, string ContentHash, bool Locked)
{
    public ProfileModPin ToPin() => new(ModId, VersionId, Locked);
}


/// <summary>
/// One revision as a history renders it: everything on its own row, and none of its mod list.
/// </summary>
/// <remarks>
/// The counts are read rather than derived. They were recorded when the revision was written, which
/// is what lets a page of fifty of these be fifty rows instead of fifty snapshot comparisons.
/// </remarks>
public record ProfileRevisionRow(
    RevisionNumber Number,
    DateTime Created,
    UserId CreatedBy,
    string? Label,
    ProfileRevisionOrigin Origin,
    ProfileId? SourceProfileId,
    RevisionNumber? SourceRevision,
    int ModCount,
    int Added,
    int Changed,
    int Removed);


/// <summary>
/// One registered version and how many of its repo's profiles pin it. A count rather than the
/// profiles themselves: the row is read for a whole catalog at once, and what the Manage page needs
/// of it is whether the number is zero.
/// </summary>
public record ModVersionUsage(ModId ModId, ModVersionId VersionId, int ProfileCount);


/// <summary>One revision that pins a mod, and which version of it.</summary>
public record ProfileRevisionDependency(ProfileId ProfileId, RevisionNumber Revision, ModVersionId VersionId);

/// <summary>One savegame version that was played on a profile revision.</summary>
public record SavegameRevisionDependency(
    Domain.Savegames.SavegameId SavegameId,
    Domain.Savegames.SavegameVersionNumber Number,
    RevisionNumber Revision);
