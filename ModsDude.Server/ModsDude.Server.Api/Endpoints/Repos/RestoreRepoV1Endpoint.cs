using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Repos;

/// <summary>
/// Brings an archived repo back, optionally under a new name.
/// </summary>
/// <remarks>
/// Repo names are globally unique among live repos, so this is the one restore where the clash can
/// come from somebody the caller has never met - a name freed by archiving is free for the whole
/// server. That is the same trade the archive makes everywhere: the name is released immediately,
/// and the cost lands here, where somebody is present to pick another.
/// </remarks>
public class RestoreRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/restore", Restore)
            .WithTags("Repos");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Restore(
        Guid repoId,
        RestoreRequest? request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
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

        RepoName? name = request?.Name is { Length: > 0 } requested
            ? new RepoName(requested)
            : null;

        var wanted = name ?? repo.Name;

        if (await dbContext.Repos.CheckNameIsTaken(wanted, repo.Id, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(wanted.Value));
        }

        repo.Restore(name);

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
