using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Drops a savegame's oldest versions once a new one has taken the head.
/// </summary>
/// <remarks>
/// <para>
/// <b>At the moment a version is minted, rather than on a sweep.</b> Pruning is caused by a
/// check-in, so doing it there keeps the cause and the effect in one place and means a history can
/// never be found long over its limit because a background job has not run yet. The work is bounded
/// - one small read and one delete, on a history that was already within its limit a moment ago.
/// </para>
/// <para>
/// <b>Rows only.</b> The blobs are left to the reclamation sweep, which already asks the one
/// question that makes deleting them safe: whether anything still refers to the address. Several
/// versions legitimately share a blob, so deleting bytes from here would mean re-asking that
/// question in this transaction and destroying somebody's save when the answer came out wrong.
/// </para>
/// <para>
/// <b>Committed separately from the version it follows.</b> A failed prune must not lose the
/// check-in that caused it: the user's play is the thing that matters and the versions this would
/// have dropped are merely still there, which the next check-in will notice.
/// </para>
/// </remarks>
internal static class SavegamePruning
{
    /// <param name="keep">
    /// How many versions to retain by recency. The plan calls for this to be configurable per repo;
    /// nothing carries that setting yet, so every repo gets
    /// <see cref="SavegameRetention.DefaultVersionsKept"/>.
    /// </param>
    public static async Task PruneAsync(
        ApplicationDbContext dbContext,
        Savegame savegame,
        CancellationToken cancellationToken,
        int keep = SavegameRetention.DefaultVersionsKept)
    {
        var rows = await dbContext.SavegameVersions.GetRetentionRowsAsync(savegame.RepoId, savegame.Id, cancellationToken);

        var prunable = SavegameRetention.PlanPrune(rows, savegame.HeadVersion, keep);

        await dbContext.SavegameVersions.DeleteVersionsAsync(savegame.RepoId, savegame.Id, prunable, cancellationToken);
    }
}
