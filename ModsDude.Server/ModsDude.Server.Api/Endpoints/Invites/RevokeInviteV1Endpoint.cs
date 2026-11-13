using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Invites;

/// <summary>
/// Takes an invite off the repo's list, switching it off first if it still works.
/// </summary>
/// <remarks>
/// <para>
/// The person who made the invite may do this, and so may any Admin. Nobody else ever sees the code,
/// so nobody else is in a position to know it has got loose.
/// </para>
/// <para>
/// <b>One route for two gestures</b>, because they are the same wish - the code should stop being on
/// my screen - and which one applies is decided by the invite rather than by the caller. An active
/// invite is revoked, which dismisses it too; a dead one is only dismissed, so an exhausted code is
/// not recorded as something somebody chose to revoke. Nothing is deleted either way: the row keeps
/// the count of who came in through the code, and keeps the code itself unusable.
/// </para>
/// </remarks>
public class RevokeInviteV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/invites/{inviteId:guid}", RevokeInvite)
            .WithTags("Invites")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> RevokeInvite(
        Guid repoId, Guid inviteId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var invite = await dbContext.RepoInvites.GetAsync(new RepoInviteId(inviteId), cancellationToken);

        // The repo in the route is the one authorization was decided against, so an invite belonging
        // to a different repo is not this caller's to touch and is reported as absent.
        if (invite is null || invite.RepoId != new RepoId(repoId))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Invite '{inviteId}' does not exist"));
        }

        var callerId = claimsPrincipal.GetUserId();

        if (invite.CreatedBy != callerId)
        {
            var authResult = await dbContext.Users.GetAsync(callerId, cancellationToken)
                .CheckIsAllowedTo(x => x
                    .AccessRepoAtLevel(invite.RepoId, RepoMembershipLevel.Admin))
                .MapToForbidden();
            if (authResult is not null)
            {
                return authResult;
            }
        }

        var now = timeService.Now();

        // Asked of the invite rather than of the caller: a code that ran out between the list being
        // drawn and the button being pressed should not be recorded as one somebody retired.
        // One stopped only by its inviter's access is revoked too: that access can come back.
        if (invite.GetOwnStatus(now) is InviteStatus.Active)
        {
            invite.Revoke(now);
        }
        else
        {
            invite.Dismiss(now);
        }

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
