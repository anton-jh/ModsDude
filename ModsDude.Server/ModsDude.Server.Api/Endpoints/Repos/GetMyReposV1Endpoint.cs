using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Api.Endpoints.Repos;

public class GetMyReposV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos", GetMyRepos)
            .WithTags("Repos");
    }


    private static async Task<Ok<IEnumerable<RepoMembershipDto>>> GetMyRepos(
        HttpContext httpContext,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        // Live repos only. Archiving is repo state rather than membership state, so an archived repo
        // leaves this list for everybody at once - see the top-level Archive.
        var reposQuery = dbContext.RepoMemberships
            .Where(x => x.UserId == userId)
            .Where(x => dbContext.Repos.Any(repo => repo.Id == x.RepoId && repo.ArchivedAt == null))
            .Join(dbContext.Repos, mem => mem.RepoId, repo => repo.Id, (mem, repo) => new
            {
                Repo = repo,
                Membership = mem
            })
            .OrderBy(x => x.Repo.Name);
        var repos = await reposQuery.ToListAsync(cancellationToken);
        var dtos = repos.Select(x => new RepoMembershipDto(
            RepoDto.FromModel(x.Repo),
            x.Membership.Level));

        return TypedResults.Ok(dtos);
    }
}
