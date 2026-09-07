using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Api.Endpoints.Repos;

/// <summary>
/// The archived repos this user is a member of - the top-level Archive.
/// </summary>
/// <remarks>
/// <para>
/// A route of its own rather than a flag on the repo list, because the two are disjoint and nothing
/// wants them merged: the sidebar shows what somebody is working in, and the Archive shows what has
/// been put away. A flag would make every caller responsible for filtering something it never asked
/// for.
/// </para>
/// <para>
/// Membership decides who sees it, exactly as it does for a live repo. Archiving takes a repo out of
/// the lists; it does not take it away from the people who were in it.
/// </para>
/// </remarks>
public class GetArchivedReposV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        // Ahead of `repos/{repoId:guid}` only in reading order - the guid constraint is what keeps
        // "archived" from being mistaken for one.
        return builder.MapGet("repos/archived", GetArchivedRepos)
            .WithTags("Repos");
    }


    private static async Task<Ok<IEnumerable<RepoMembershipDto>>> GetArchivedRepos(
        HttpContext httpContext,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        var rows = await dbContext.RepoMemberships
            .Where(x => x.UserId == userId)
            .Join(dbContext.Repos, mem => mem.RepoId, repo => repo.Id, (mem, repo) => new
            {
                Repo = repo,
                Membership = mem
            })
            .Where(x => x.Repo.ArchivedAt != null)
            // Most recently archived first: the one somebody is looking for is almost always the one
            // they just put away, and several archived repos may share a name.
            .OrderByDescending(x => x.Repo.ArchivedAt)
            .ThenBy(x => x.Repo.Name)
            .ToListAsync(cancellationToken);

        var dtos = rows.Select(x => new RepoMembershipDto(RepoDto.FromModel(x.Repo), x.Membership.Level));

        return TypedResults.Ok(dtos);
    }
}
