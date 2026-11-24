using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>What every label a claim writes says about the repo and the person adding the blob.</summary>
public sealed record BlobLabelContext(RepoId RepoId, string RepoName, AdapterIdentifier Game, UserId AddedBy, string AddedByName)
{
    public static async Task<BlobLabelContext> ReadAsync(ApplicationDbContext dbContext, RepoId repoId, UserId userId, CancellationToken cancellationToken)
    {
        var repo = await dbContext.Repos
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(x => x.Id == repoId)
            .Select(x => new { x.Name, Game = x.AdapterData.Id })
            .SingleAsync(cancellationToken);

        var user = await dbContext.Users
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(x => x.Id == userId)
            .Select(x => x.DisplayName)
            .SingleAsync(cancellationToken);

        return new BlobLabelContext(repoId, repo.Name.Value, repo.Game, userId, user.Value);
    }
}
