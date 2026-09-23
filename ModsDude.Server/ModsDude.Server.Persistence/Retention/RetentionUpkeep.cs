using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Retention;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Retention;

/// <summary>
/// Clears the schedules a write has just made wrong, so a row stops showing a deletion date the moment
/// something starts needing it rather than the next morning.
/// </summary>
/// <remarks>
/// <para>
/// <b>Clears, never schedules.</b> Making new schedules is the daily job's, and nothing here needs to
/// be prompt about it: a row that has become eligible loses nothing by waiting a day to be told so. A
/// row that has stopped being eligible and still shows a date is the thing worth fixing straight away.
/// </para>
/// <para>
/// <b>After the write has committed, and never fatal to it.</b> What decides the answer is the state
/// the write produced, so it has to be visible to be read. And the write is what the person asked
/// for: a schedule left standing is cleared by the deletion job before it deletes anything, and by the
/// next scheduling run, so failing here costs a date shown for a few hours - it is logged rather than
/// reported, and nothing is ever deleted because of it.
/// </para>
/// </remarks>
public class RetentionUpkeep(ApplicationDbContext dbContext, ITimeService timeService, ILogger<RetentionUpkeep> logger)
{
    /// <summary>After a savegame gained or lost a snapshot.</summary>
    public Task ReleaseSavegameAsync(RepoId repoId, SavegameId savegameId, CancellationToken cancellationToken)
    {
        return GuardAsync("savegame", savegameId.Value.ToString(), async () =>
        {
            foreach (var (id, history) in await RetentionHistories.LoadSavegamesAsync(dbContext, repoId, savegameId, cancellationToken))
            {
                await RetentionHistories.ApplySavegameAsync(dbContext, repoId, id, history.StaleChanges(), cancellationToken);
            }
        });
    }

    /// <summary>After a profile gained or lost a revision, or a snapshot was played on one of them.</summary>
    public Task ReleaseProfileAsync(RepoId repoId, ProfileId profileId, CancellationToken cancellationToken)
    {
        return GuardAsync("profile", profileId.Value.ToString(), async () =>
        {
            foreach (var (id, history) in await RetentionHistories.LoadProfilesAsync(dbContext, repoId, profileId, cancellationToken))
            {
                await RetentionHistories.ApplyProfileAsync(dbContext, repoId, id, history.StaleChanges(), cancellationToken);
            }
        });
    }

    /// <summary>After mods gained, lost or reordered a version, or started being pinned.</summary>
    public Task ReleaseModsAsync(RepoId repoId, IReadOnlyCollection<ModId> modIds, CancellationToken cancellationToken)
    {
        return GuardAsync("mods", string.Join(", ", modIds.Select(x => x.Value)), async () =>
        {
            DateTimeOffset now = timeService.Now();

            foreach (var (id, history) in await RetentionHistories.LoadModsAsync(dbContext, repoId, modIds, cancellationToken))
            {
                await RetentionHistories.ApplyModAsync(dbContext, repoId, id, history.StaleChanges(), now, cancellationToken);
            }
        });
    }

    /// <summary>
    /// After a revision was saved: the mods it pins that have a version scheduled. Asked of the
    /// database first because a revision can pin thousands of mods and almost none of them will have
    /// anything scheduled - so the question is which few to look at, not the whole list.
    /// </summary>
    public Task ReleaseModsPinnedByAsync(RepoId repoId, ProfileId profileId, RevisionNumber revision, CancellationToken cancellationToken)
    {
        return GuardAsync("revision", $"{profileId.Value}/{revision.Value}", async () =>
        {
            var pinned = dbContext.ProfileRevisions
                .Where(x => x.RepoId == repoId && x.ProfileId == profileId && x.Number == revision)
                .SelectMany(x => x.ModDependencies)
                .Select(x => x.ModVersion.ModId);

            var affected = await dbContext.ModVersions
                .Where(x => x.RepoId == repoId && x.DeletionScheduledFor != null && pinned.Contains(x.ModId))
                .Select(x => x.ModId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (affected.Count > 0)
            {
                await ReleaseModsAsync(repoId, affected, cancellationToken);
            }
        });
    }


    private async Task GuardAsync(string subject, string id, Func<Task> release)
    {
        try
        {
            await release();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Clearing stale deletion schedules for {Subject} {Id} failed; the next scheduling run will.", subject, id);
        }
    }
}
