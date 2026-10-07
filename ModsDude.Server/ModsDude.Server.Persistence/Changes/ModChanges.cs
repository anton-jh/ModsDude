using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Changes;

/// <summary>
/// What changed in a repo's mods after a change number: the versions as they are now, the ones
/// deleted, and the number to ask from next time.
/// </summary>
/// <param name="HasMore">Whether the versions were cut at the page size; ask again from <paramref name="Sequence"/>.</param>
public sealed record ModChanges(
    IReadOnlyList<ModVersion> Versions,
    IReadOnlyList<ModVersionDeletion> Deleted,
    long Sequence,
    bool HasMore)
{
    public const string SequenceColumn = "ChangeSequence";


    /// <param name="after">The <see cref="Sequence"/> of an earlier read, or 0 for everything.</param>
    public static async Task<ModChanges> ReadAsync(
        ApplicationDbContext dbContext,
        RepoId repoId,
        long after,
        int limit,
        CancellationToken cancellationToken)
    {
        // First, and in a statement of its own: every change numbered up to the counter has committed,
        // so the reads below see all of them. Reading the counter last could claim a change they
        // missed.
        var sequence = await dbContext.RepoChangeCounters
            .Where(x => x.RepoId == repoId)
            .Select(x => x.Mods)
            .FirstOrDefaultAsync(cancellationToken);

        var versions = await dbContext.ModVersions
            .Where(x => x.RepoId == repoId
                && EF.Property<long>(x, SequenceColumn) > after
                && EF.Property<long>(x, SequenceColumn) <= sequence)
            .OrderBy(x => EF.Property<long>(x, SequenceColumn))
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = versions.Count > limit;

        if (hasMore)
        {
            versions.RemoveAt(versions.Count - 1);
            sequence = dbContext.Entry(versions[^1]).Property<long>(SequenceColumn).CurrentValue;
        }

        var deleted = await dbContext.ModVersionDeletions
            .Where(x => x.RepoId == repoId && x.ChangeSequence > after && x.ChangeSequence <= sequence)
            .OrderBy(x => x.ChangeSequence)
            .ToListAsync(cancellationToken);

        return new ModChanges(versions, deleted, sequence, hasMore);
    }
}
