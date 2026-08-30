using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Repos;

public class DeleteRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repo/{repoId:guid}", DeleteRepo)
            .WithTags("Repos");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> DeleteRepo(
        Guid repoId,
        ClaimsPrincipal claimsPrincipal,
        IUnitOfWork unitOfWork,
        ApplicationDbContext dbContext,
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

        // The ModVersion -> Repo foreign key is Restrict, so deleting a repo that still has mods
        // fails at the database with an unhandled exception. Refuse it here instead, with a problem
        // the client can act on. Note that mod blobs are not reclaimed by this endpoint either way.
        if (await dbContext.ModVersions.AnyAsync(x => x.RepoId == new RepoId(repoId), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.RepoNotEmpty(new RepoId(repoId)));
        }

        dbContext.Repos.Remove(repo);
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
