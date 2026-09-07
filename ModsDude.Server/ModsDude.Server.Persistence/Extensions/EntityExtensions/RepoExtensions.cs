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
    /// Deletes everything in a repo that will not fall out of the way when the repo's own row goes.
    /// The caller removes that row and commits, both inside a transaction it owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order is the whole method.</b> Three foreign keys inside a repo are <c>Restrict</c>
    /// rather than <c>Cascade</c>, each so that deleting one thing cannot rewrite another's record:
    /// a savegame version names the profile revision it was played on, a revision pins mod versions,
    /// and a mod version belongs to the repo. Removing the repo row on its own walks into the
    /// innermost of them, so the dependants go first and each step frees the next — savegames (their
    /// versions and their claims cascade with them), then revisions (their mod dependencies cascade
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
    public static async Task EmptyAsync(this ApplicationDbContext dbContext, RepoId repoId, CancellationToken cancellationToken)
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
