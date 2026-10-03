using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Extensions.EntityExtensions;
public static class RepoExtensions
{
    public static object[] GetKey(this Repo repo)
    {
        return [repo.Id];
    }

    public static object[] GetKey(RepoId repoId)
    {
        return [repoId];
    }

    // No CheckNameIsTaken here, unlike profiles and savegames: a repo name is never taken. Those two
    // are unique within their repo because a profile is picked by name out of a list the whole group
    // shares; a repo is only ever arrived at through an invite code.

    public static async Task<Repo?> GetAsync(this DbSet<Repo> dbSet, RepoId repoId, CancellationToken cancellationToken)
    {
        return await dbSet.FindAsync(GetKey(repoId), cancellationToken);
    }

    /// <summary>
    /// Deletes the repo and everything in it, in one transaction, so no request and no crash can see
    /// its contents gone while its row still stands. Nothing inside can refuse it: it takes the
    /// dependants and the dependencies together. The caller decides whether the repo may go.
    /// </summary>
    /// <remarks>
    /// The blobs stay. They are addressed by content and shared, so the reclamation sweep removes
    /// them once nothing refers to them.
    /// </remarks>
    public static async Task DeleteWithContentsAsync(this ApplicationDbContext dbContext, Repo repo, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.EmptyAsync(repo.Id, cancellationToken);

        dbContext.Repos.Remove(repo);
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes everything in a repo that will not fall out of the way when the repo's own row goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order is the whole method.</b> Three foreign keys inside a repo are <c>Restrict</c>
    /// rather than <c>Cascade</c>, each so that deleting one thing cannot rewrite another's record:
    /// a savegame snapshot names the profile revision it was played on, a revision pins mod versions,
    /// and a mod version belongs to the repo. Removing the repo row on its own walks into the
    /// innermost of them, so the dependants go first and each step frees the next — savegames (their
    /// snapshots and their claims cascade with them), then revisions (their mod dependencies cascade
    /// with them), then the mod versions nothing pins any more. Profiles, memberships and invites
    /// are left to the repo's own cascade, in the database where it belongs.
    /// </para>
    /// <para>
    /// <c>ExecuteDelete</c> rather than loading the entities, for the reason
    /// <see cref="ProfileRevisionExtensions.DeleteRevisionsAsync"/> gives one aggregate down and
    /// more so here: a repo is the scale at which materializing means reading every mod version and
    /// every dependency row the group has ever had, to send them back one at a time.
    /// </para>
    /// </remarks>
    private static async Task EmptyAsync(this ApplicationDbContext dbContext, RepoId repoId, CancellationToken cancellationToken)
    {
        await dbContext.Savegames
            .Where(x => x.RepoId == repoId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ProfileRevisions
            .Where(x => x.RepoId == repoId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ModVersions
            .Where(x => x.RepoId == repoId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
