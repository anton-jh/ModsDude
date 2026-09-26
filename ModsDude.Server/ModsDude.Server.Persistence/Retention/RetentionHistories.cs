using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Retention;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Retention;

/// <summary>
/// One history as retention sees it: which rows are eligible now, and which carry a schedule.
/// </summary>
internal record RetentionHistory<TKey>(
    IReadOnlyDictionary<TKey, DeletionReason> Eligible,
    IReadOnlyDictionary<TKey, DeletionSchedule> Scheduled)
    where TKey : notnull
{
    /// <summary>Clears every schedule that no longer stands, and makes none.</summary>
    public RetentionChanges<TKey> StaleChanges()
    {
        return new RetentionChanges<TKey>(
            new Dictionary<TKey, DeletionSchedule>(),
            RetentionPolicy.FindStale(Eligible, Scheduled));
    }
}


/// <summary>
/// Reads the histories retention reasons about and writes the schedules it decides on. Everything
/// the <see cref="RetentionPolicy"/> needs is read per repo in a handful of narrow queries - numbers,
/// dates and the facts that hold a row - and never by materializing an entity.
/// </summary>
/// <remarks>
/// <b>Every repo, profile and savegame, archived or not.</b> Archiving is putting something away, not
/// asking for its history to be kept, so an archived savegame winds down like any other.
/// </remarks>
internal static class RetentionHistories
{
    /// <param name="only">One savegame, or <c>null</c> for every savegame in the repo.</param>
    public static async Task<Dictionary<SavegameId, RetentionHistory<SavegameSnapshotNumber>>> LoadSavegamesAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        SavegameId? only,
        CancellationToken cancellationToken)
    {
        var snapshots = dbContext.SavegameSnapshots.AsNoTracking().Where(x => x.RepoId == repoId);
        var savegames = dbContext.Savegames.AsNoTracking().Where(x => x.RepoId == repoId);

        if (only is { } savegameId)
        {
            snapshots = snapshots.Where(x => x.SavegameId == savegameId);
            savegames = savegames.Where(x => x.Id == savegameId);
        }

        var rows = await snapshots
            .Select(x => new { x.SavegameId, x.Number, x.DeletionScheduledFor, x.DeletionReason })
            .ToListAsync(cancellationToken);

        var heads = await savegames
            .Select(x => new { x.Id, x.HeadSnapshot })
            .ToDictionaryAsync(x => x.Id, x => x.HeadSnapshot, cancellationToken);

        return rows
            .GroupBy(x => x.SavegameId)
            .ToDictionary(
                x => x.Key,
                x => Build(
                    x.Select(y => (y.Number, (long)y.Number.Value, IsHeld: false, y.DeletionScheduledFor, y.DeletionReason)),
                    RetentionPolicy.SavegameSnapshots,
                    heads.TryGetValue(x.Key, out var head) ? head : null));
    }

    /// <param name="only">One profile, or <c>null</c> for every profile in the repo.</param>
    public static async Task<Dictionary<ProfileId, RetentionHistory<RevisionNumber>>> LoadProfilesAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        ProfileId? only,
        CancellationToken cancellationToken)
    {
        var revisions = dbContext.ProfileRevisions.AsNoTracking().Where(x => x.RepoId == repoId);
        var profiles = dbContext.Profiles.AsNoTracking().Where(x => x.RepoId == repoId);
        var snapshots = dbContext.SavegameSnapshots.AsNoTracking().Where(x => x.RepoId == repoId && x.ProfileId != null);

        if (only is { } profileId)
        {
            revisions = revisions.Where(x => x.ProfileId == profileId);
            profiles = profiles.Where(x => x.Id == profileId);
            snapshots = snapshots.Where(x => x.ProfileId == profileId);
        }

        var rows = await revisions
            .Select(x => new { x.ProfileId, x.Number, x.DeletionScheduledFor, x.DeletionReason })
            .ToListAsync(cancellationToken);

        var heads = await profiles
            .Select(x => new { x.Id, x.HeadRevision })
            .ToDictionaryAsync(x => x.Id, x => x.HeadRevision, cancellationToken);

        // Deduplicated in the database: a revision a savegame was played on for a month is named by
        // thirty snapshots, and the question is only whether any of them names it.
        var played = (await snapshots
            .Select(x => new { x.ProfileId, x.ProfileRevision })
            .Distinct()
            .ToListAsync(cancellationToken))
            .Select(x => (x.ProfileId!.Value, x.ProfileRevision!.Value))
            .ToHashSet();

        // An open claim is play that no snapshot names yet. Deleting a revision under it would leave
        // the check-in naming a revision that no longer exists, which is refused, forced or not.
        var checkedOut = (await dbContext.SavegameCheckouts.GetCheckoutRevisionHoldsAsync(dbContext.Savegames, repoId, only, cancellationToken))
            .ToLookup(x => x.ProfileId);

        return rows
            .GroupBy(x => x.ProfileId)
            .ToDictionary(
                x => x.Key,
                x => Build(
                    x.Select(y => (
                        y.Number,
                        (long)y.Number.Value,
                        played.Contains((y.ProfileId, y.Number)) || checkedOut[y.ProfileId].Any(hold => hold.Holds(y.Number)),
                        y.DeletionScheduledFor,
                        y.DeletionReason)),
                    RetentionPolicy.ProfileRevisions,
                    heads.TryGetValue(x.Key, out var head) ? head : null));
    }

    /// <param name="only">The named mods, or <c>null</c> for every mod in the repo.</param>
    public static async Task<Dictionary<ModId, RetentionHistory<ModVersionId>>> LoadModsAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        IReadOnlyCollection<ModId>? only,
        CancellationToken cancellationToken)
    {
        var versions = dbContext.ModVersions.AsNoTracking().Where(x => x.RepoId == repoId);
        var pins = dbContext.ProfileRevisions.AsNoTracking()
            .Where(x => x.RepoId == repoId)
            .SelectMany(x => x.ModDependencies)
            .Select(x => new { x.ModVersion.ModId, x.ModVersion.Id });

        if (only is not null)
        {
            if (only.Count == 0)
            {
                return [];
            }

            versions = versions.Where(x => only.Contains(x.ModId));
            pins = pins.Where(x => only.Contains(x.ModId));
        }

        var rows = await versions
            .Select(x => new { x.ModId, x.Id, x.SequenceNumber, x.DeletionScheduledFor, x.DeletionReason })
            .ToListAsync(cancellationToken);

        // Every revision's pins, not only the heads': that is what the foreign key holds, and what
        // keeps an old revision - and any savegame played on it - reproducible.
        var pinned = (await pins.Distinct().ToListAsync(cancellationToken))
            .Select(x => (x.ModId, x.Id))
            .ToHashSet();

        return rows
            .GroupBy(x => x.ModId)
            .ToDictionary(
                x => x.Key,
                x => Build(
                    x.Select(y => (y.Id, (long)y.SequenceNumber, pinned.Contains((y.ModId, y.Id)), y.DeletionScheduledFor, y.DeletionReason)),
                    RetentionPolicy.ModVersions,
                    head: null));
    }


    public static async Task ApplySavegameAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, SavegameId savegameId,
        RetentionChanges<SavegameSnapshotNumber> changes,
        CancellationToken cancellationToken)
    {
        foreach (var (date, reason, numbers) in Group(changes))
        {
            await dbContext.SavegameSnapshots
                .Where(x => x.RepoId == repoId && x.SavegameId == savegameId && numbers.Contains(x.Number))
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.DeletionScheduledFor, date)
                    .SetProperty(y => y.DeletionReason, reason),
                    cancellationToken);
        }
    }

    public static async Task ApplyProfileAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, ProfileId profileId,
        RetentionChanges<RevisionNumber> changes,
        CancellationToken cancellationToken)
    {
        foreach (var (date, reason, numbers) in Group(changes))
        {
            await dbContext.ProfileRevisions
                .Where(x => x.RepoId == repoId && x.ProfileId == profileId && numbers.Contains(x.Number))
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.DeletionScheduledFor, date)
                    .SetProperty(y => y.DeletionReason, reason),
                    cancellationToken);
        }
    }

    /// <param name="timestamp">
    /// What <see cref="ModVersion.Updated"/> moves to. A version is the one row here a client caches
    /// through a delta feed ordered by that column, so a schedule that did not move it would never
    /// reach a client that already holds the version.
    /// </param>
    public static async Task ApplyModAsync(
        ApplicationDbContext dbContext,
        RepoId repoId, ModId modId,
        RetentionChanges<ModVersionId> changes,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        foreach (var (date, reason, ids) in Group(changes))
        {
            await dbContext.ModVersions
                .Where(x => x.RepoId == repoId && x.ModId == modId && ids.Contains(x.Id))
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.DeletionScheduledFor, date)
                    .SetProperty(y => y.DeletionReason, reason)
                    .SetProperty(y => y.Updated, timestamp),
                    cancellationToken);
        }
    }


    private static RetentionHistory<TKey> Build<TKey>(
        IEnumerable<(TKey Key, long Order, bool IsHeld, DateOnly? Date, DeletionReason? Reason)> rows,
        RetentionRule rule,
        TKey? head)
        where TKey : struct
    {
        var materialized = rows.ToList();

        var eligible = RetentionPolicy
            .Evaluate(materialized.Select(x => new RetentionCandidate<TKey>(x.Key, x.Order, x.IsHeld)), rule.Window)
            // The head is the newest row everywhere it exists, so the policy already keeps it. Said
            // again because a history whose head sat anywhere else would otherwise lose the one row
            // that is what the savegame or profile currently is.
            .Where(x => head is null || !x.Key.Equals(head.Value))
            .ToDictionary(x => x.Key, x => x.Value);

        var scheduled = materialized
            .Where(x => x.Date is not null && x.Reason is not null)
            .ToDictionary(x => x.Key, x => new DeletionSchedule(x.Date!.Value, x.Reason!.Value));

        return new RetentionHistory<TKey>(eligible, scheduled);
    }

    /// <summary>
    /// The changes as one update per distinct outcome - clearing, or one date and reason - rather than
    /// one per row. A history scheduled in one pass shares its date, so this is usually one or two
    /// statements however many rows moved.
    /// </summary>
    private static IEnumerable<(DateOnly? Date, DeletionReason? Reason, List<TKey> Keys)> Group<TKey>(RetentionChanges<TKey> changes)
        where TKey : notnull
    {
        if (changes.Unschedule.Count > 0)
        {
            yield return (null, null, [.. changes.Unschedule]);
        }

        foreach (var group in changes.Schedule.GroupBy(x => x.Value))
        {
            yield return (group.Key.Date, group.Key.Reason, [.. group.Select(x => x.Key)]);
        }
    }
}
