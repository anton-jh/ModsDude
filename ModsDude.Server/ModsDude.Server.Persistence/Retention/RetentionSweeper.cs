using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Retention;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Persistence.Retention;

/// <summary>
/// The two daily retention jobs: one that schedules what the <see cref="RetentionPolicy"/> says may
/// go, and one that deletes what has come due.
/// </summary>
/// <remarks>
/// <para>
/// <b>Repo by repo, and a failure in one does not stop the rest.</b> A sweep that failed deleted less
/// than it could have, which is the harmless direction, and tomorrow's run picks up where it left off.
/// </para>
/// <para>
/// <b>Deletion asks the policy again</b> rather than trusting the dates. A schedule is only cleared
/// eventually - by a write that noticed, or by the scheduling run - and a deletion cannot be taken
/// back, so a row goes only if it is still eligible now for the reason it was scheduled for.
/// </para>
/// <para>
/// <b>And clears what no longer stands before it deletes anything.</b> Otherwise a stale date could
/// come back to life: a row dated as winding down, whose history then grew, is rightly kept - but once
/// tonight's deletions shrink the history again it is eligible for that reason once more, and a second
/// run the same day would delete it on a date made for a decision that had lapsed. Clearing first is
/// what makes a rerun find nothing, so the job is idempotent however the writes before it went, and the
/// write-time clearing in <see cref="RetentionUpkeep"/> only ever affects what the dates look like.
/// </para>
/// <para>
/// <b>Rows only.</b> Savegame and mod blobs are left to the reclamation sweep, which already asks the
/// one question that makes deleting bytes safe: whether anything still refers to the address.
/// </para>
/// </remarks>
public class RetentionSweeper(ApplicationDbContext dbContext, ILogger<RetentionSweeper> logger)
{
    /// <param name="today">The date the schedules count from, in the zone the jobs run in.</param>
    /// <param name="now">What a rescheduled mod version's <see cref="ModVersion.Updated"/> moves to.</param>
    public async Task ScheduleAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var totals = new SweepTotals();

        foreach (var repoId in await GetRepoIdsAsync(cancellationToken))
        {
            try
            {
                await ScheduleRepoAsync(repoId, today, now, totals, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Scheduling retention for repo {RepoId} failed.", repoId.Value);
            }
        }

        logger.LogInformation(
            "Retention scheduled {Scheduled} and unscheduled {Unscheduled} snapshots, revisions and mod versions.",
            totals.Scheduled, totals.Unscheduled);
    }

    /// <param name="today">Anything scheduled for this date or earlier is due.</param>
    /// <param name="now">What the versions left behind a deleted mod version are stamped with.</param>
    public async Task DeleteDueAsync(DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var totals = new SweepTotals();

        foreach (var repoId in await GetRepoIdsAsync(cancellationToken))
        {
            // In the order things hold each other: a snapshot holds a revision, a revision holds mod
            // versions. Deleting in the other order would only ever find less to delete, never more,
            // but this way round a row's deletion is never blocked by something going the same night.
            await DeleteSnapshotsAsync(repoId, today, totals, cancellationToken);
            await DeleteRevisionsAsync(repoId, today, totals, cancellationToken);
            await DeleteModVersionsAsync(repoId, today, now, totals, cancellationToken);
        }

        logger.LogInformation(
            "Retention deleted {Snapshots} snapshots, {Revisions} revisions and {ModVersions} mod versions.",
            totals.Snapshots, totals.Revisions, totals.ModVersions);
    }


    private async Task ScheduleRepoAsync(RepoId repoId, DateOnly today, DateTimeOffset now, SweepTotals totals, CancellationToken cancellationToken)
    {
        foreach (var (savegameId, history) in await RetentionHistories.LoadSavegamesAsync(dbContext, repoId, null, cancellationToken))
        {
            var changes = RetentionPolicy.Reconcile(history.Eligible, history.Scheduled, RetentionPolicy.SavegameSnapshots, today);
            await RetentionHistories.ApplySavegameAsync(dbContext, repoId, savegameId, changes, cancellationToken);
            totals.Count(changes);
        }

        foreach (var (profileId, history) in await RetentionHistories.LoadProfilesAsync(dbContext, repoId, null, cancellationToken))
        {
            var changes = RetentionPolicy.Reconcile(history.Eligible, history.Scheduled, RetentionPolicy.ProfileRevisions, today);
            await RetentionHistories.ApplyProfileAsync(dbContext, repoId, profileId, changes, cancellationToken);
            totals.Count(changes);
        }

        foreach (var (modId, history) in await RetentionHistories.LoadModsAsync(dbContext, repoId, null, cancellationToken))
        {
            var changes = RetentionPolicy.Reconcile(history.Eligible, history.Scheduled, RetentionPolicy.ModVersions, today);
            await RetentionHistories.ApplyModAsync(dbContext, repoId, modId, changes, now, cancellationToken);
            totals.Count(changes);
        }
    }

    private async Task DeleteSnapshotsAsync(RepoId repoId, DateOnly today, SweepTotals totals, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var (savegameId, history) in await RetentionHistories.LoadSavegamesAsync(dbContext, repoId, null, cancellationToken))
            {
                await RetentionHistories.ApplySavegameAsync(dbContext, repoId, savegameId, history.StaleChanges(), cancellationToken);

                var due = RetentionPolicy.FindDue(history.Eligible, history.Scheduled, today);
                totals.Snapshots += await dbContext.SavegameSnapshots.DeleteSnapshotsAsync(repoId, savegameId, due, cancellationToken);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Deleting due snapshots in repo {RepoId} failed.", repoId.Value);
        }
    }

    private async Task DeleteRevisionsAsync(RepoId repoId, DateOnly today, SweepTotals totals, CancellationToken cancellationToken)
    {
        Dictionary<ProfileId, RetentionHistory<RevisionNumber>> histories;

        try
        {
            histories = await RetentionHistories.LoadProfilesAsync(dbContext, repoId, null, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Reading revisions in repo {RepoId} for deletion failed.", repoId.Value);
            return;
        }

        foreach (var (profileId, history) in histories)
        {
            var due = RetentionPolicy.FindDue(history.Eligible, history.Scheduled, today);

            try
            {
                await RetentionHistories.ApplyProfileAsync(dbContext, repoId, profileId, history.StaleChanges(), cancellationToken);

                totals.Revisions += await dbContext.ProfileRevisions.DeleteRevisionsAsync(repoId, profileId, due, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Most likely a check-in played on one of these between the read and the delete, which
                // the foreign key refuses. Tomorrow's scheduling run will see the snapshot and let it be.
                logger.LogWarning(exception, "Deleting due revisions of profile {ProfileId} in repo {RepoId} failed.", profileId.Value, repoId.Value);
            }
        }
    }

    private async Task DeleteModVersionsAsync(RepoId repoId, DateOnly today, DateTimeOffset now, SweepTotals totals, CancellationToken cancellationToken)
    {
        Dictionary<ModId, RetentionHistory<ModVersionId>> histories;

        try
        {
            histories = await RetentionHistories.LoadModsAsync(dbContext, repoId, null, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Reading mod versions in repo {RepoId} for deletion failed.", repoId.Value);
            return;
        }

        foreach (var (modId, history) in histories)
        {
            var due = RetentionPolicy.FindDue(history.Eligible, history.Scheduled, today).ToHashSet();

            try
            {
                await RetentionHistories.ApplyModAsync(dbContext, repoId, modId, history.StaleChanges(), now, cancellationToken);

                if (due.Count > 0)
                {
                    totals.ModVersions += await DeleteVersionsOfModAsync(repoId, modId, due, now, cancellationToken);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Most likely a profile pinned one of these between the read and the delete, which the
                // foreign key refuses. Whatever was tracked for this mod is dropped so the next mod
                // starts clean.
                dbContext.ChangeTracker.Clear();
                logger.LogWarning(exception, "Deleting due versions of mod {ModId} in repo {RepoId} failed.", modId.Value, repoId.Value);
            }
        }
    }

    /// <summary>
    /// One version per commit, the way the delete endpoint does it: the sequence numbers are kept
    /// contiguous by a unique index, and closing one gap at a time is the path that is known to
    /// satisfy it at every statement.
    /// </summary>
    private async Task<int> DeleteVersionsOfModAsync(RepoId repoId, ModId modId, IReadOnlySet<ModVersionId> due, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var siblings = await dbContext.ModVersions.GetVersionsOfModAsync(repoId, modId, cancellationToken);
        var deleted = 0;

        // Newest first, so the versions that shift down to close each gap are never ones about to go.
        foreach (var version in siblings.Where(x => due.Contains(x.Id)).OrderByDescending(x => x.SequenceNumber).ToList())
        {
            siblings.Remove(version);

            dbContext.ModVersions.Remove(version);
            ModVersionSequencer.CloseGap(siblings, version, now);

            await dbContext.SaveChangesAsync(cancellationToken);
            deleted++;
        }

        dbContext.ChangeTracker.Clear();

        return deleted;
    }

    private async Task<List<RepoId>> GetRepoIdsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Repos.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
    }


    private class SweepTotals
    {
        public int Scheduled { get; set; }
        public int Unscheduled { get; set; }
        public int Snapshots { get; set; }
        public int Revisions { get; set; }
        public int ModVersions { get; set; }

        public void Count<TKey>(RetentionChanges<TKey> changes) where TKey : notnull
        {
            Scheduled += changes.Schedule.Count;
            Unscheduled += changes.Unschedule.Count;
        }
    }
}
