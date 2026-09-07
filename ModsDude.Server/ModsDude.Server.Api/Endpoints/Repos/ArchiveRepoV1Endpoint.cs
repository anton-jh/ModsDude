using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Repos;

/// <summary>
/// Puts a repo away.
/// </summary>
/// <remarks>
/// <para>
/// <b>Repo state, not membership state.</b> There is no per-person version of this: archiving a repo
/// takes it out of the list for every member at once, and it turns up in everybody's top-level
/// Archive. A repo is the shared thing, so putting it away is a shared act.
/// </para>
/// <para>
/// <b>The only way to make a repo go away.</b> Deleting one is refused until it is archived and
/// emptied - a repo carries the group's entire catalog, every profile's history and every savegame,
/// and none of that comes back.
/// </para>
/// </remarks>
public class ArchiveRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/archive", Archive)
            .WithTags("Repos");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Archive(
        Guid repoId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Admin))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var repo = await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken);
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        repo.Archive(timeService.Now());

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
