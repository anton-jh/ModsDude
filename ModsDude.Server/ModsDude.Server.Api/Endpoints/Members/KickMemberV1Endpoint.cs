using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Members;

public class KickMemberV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/members/{userId}", KickMember)
            .WithTags("Members");
    }


    private async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> KickMember(
        Guid repoId, string userId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        // ChangeOthersMembership needs the subject's level to know what it needs, so it cannot run
        // before the load. What can run before it is the floor that check can never fall below —
        // Member, the level it demands to touch even a Guest — which is enough to stop a
        // non-member learning from the responses which repo ids and memberships exist.
        var user = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken);

        var authResult = user
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var repo = await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken);
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Repo '{repoId}' does not exist"));
        }

        var subjectMembership = repo.GetMembership(new UserId(userId));
        if (subjectMembership is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Member '{userId}' not found"));
        }

        authResult = user
            .CheckIsAllowedTo(x => x
                .ChangeOthersMembership(subjectMembership))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        if (repo.IsOnlyAdmin(new UserId(userId)))
        {
            return TypedResults.BadRequest(Problems.CannotKickOnlyAdmin);
        }

        repo.KickMember(new UserId(userId));
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
