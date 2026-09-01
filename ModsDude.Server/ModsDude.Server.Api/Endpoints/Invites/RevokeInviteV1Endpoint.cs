using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Invites;

/// <summary>
/// Switches an invite off for good.
/// </summary>
/// <remarks>
/// Any Member can revoke any of the repo's invites, including one an Admin made. Revoking only ever
/// takes access away, and a code loose in the world is the kind of thing that wants stopping by
/// whoever notices it rather than by whoever is senior enough.
/// </remarks>
public class RevokeInviteV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/invites/{inviteId:guid}", RevokeInvite)
            .WithTags("Invites");
    }


    private async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> RevokeInvite(
        Guid repoId, Guid inviteId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var invite = await dbContext.RepoInvites.GetAsync(new RepoInviteId(inviteId), cancellationToken);

        // The repo in the route is the one authorization was decided against, so an invite belonging
        // to a different repo is not this caller's to touch and is reported as absent.
        if (invite is null || invite.RepoId != new RepoId(repoId))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Invite '{inviteId}' does not exist"));
        }

        invite.Revoke();
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
