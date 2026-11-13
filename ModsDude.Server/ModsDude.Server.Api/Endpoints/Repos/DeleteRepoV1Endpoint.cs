using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Repos;

/// <summary>
/// Deletes an archived repo and everything in it - the whole mod catalog, every profile with its
/// history, every savegame with its versions and its claim log, the invites and the memberships.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing inside can refuse it.</b> The rules that stand between a single mod version, profile
/// or savegame and deletion all exist to stop one of them being taken out from under the others -
/// a revision that pins a version, a savegame played on a revision. Deleting the repo takes the
/// dependants and the dependencies together, so there is nothing left to protect and no partial
/// state to protect it from. What makes this safe is not a check on the contents but that a repo
/// can only be deleted once it has been archived, by an Admin: two deliberate acts, and the first
/// one is visible to every member for as long as they care to notice.
/// </para>
/// <para>
/// <b>Authorized here rather than on the route.</b> A repeat of a delete that succeeded finds no repo
/// and the caller in it no longer, which the route's check would refuse. Already gone is what was
/// asked for, so it is answered Ok - which tells anybody only that no repo has that id.
/// </para>
/// </remarks>
public class DeleteRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}", DeleteRepo)
            .WithTags("Repos");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> DeleteRepo(
        Guid repoId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var repo = await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken);
        if (repo is null)
        {
            return TypedResults.Ok();
        }

        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(repo.Id, RepoMembershipLevel.Admin))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        // Reached from the top-level Archive and nowhere else. A repo carries the group's whole
        // catalog, every profile's history and every savegame, and none of it comes back.
        if (repo.IsArchived is false)
        {
            return TypedResults.BadRequest(Problems.NotArchived("Repo", repoId));
        }

        try
        {
            await dbContext.DeleteWithContentsAsync(repo, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.BadRequest(Problems.RepoChanged);
        }

        return TypedResults.Ok();
    }
}
