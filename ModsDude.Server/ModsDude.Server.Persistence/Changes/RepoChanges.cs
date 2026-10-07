using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Changes;

/// <summary>Where one repo's change counters stand. A repo that has never been counted is all zeros.</summary>
public sealed record RepoChanges(
    RepoId RepoId,
    long Repo,
    long Profiles,
    long Savegames,
    long Mods,
    long Members,
    long Activity)
{
    /// <summary>The counters of every live repo <paramref name="userId"/> is a member of, ordered by repo id.</summary>
    /// <remarks>The same repos the caller's repo list holds, so a repo joined, left or archived shows as one appearing or going.</remarks>
    public static async Task<List<RepoChanges>> ReadForAsync(
        ApplicationDbContext dbContext,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.RepoMemberships
            .Where(x => x.UserId == userId)
            .Where(x => dbContext.Repos.Any(repo => repo.Id == x.RepoId && repo.ArchivedAt == null))
            .Select(x => new
            {
                x.RepoId,
                Counter = dbContext.RepoChangeCounters.FirstOrDefault(counter => counter.RepoId == x.RepoId)
            })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Select(x => x.Counter is { } counter
                ? new RepoChanges(x.RepoId, counter.Repo, counter.Profiles, counter.Savegames, counter.Mods, counter.Members, counter.Activity)
                : new RepoChanges(x.RepoId, 0, 0, 0, 0, 0, 0))
            .OrderBy(x => x.RepoId.Value)];
    }
}
