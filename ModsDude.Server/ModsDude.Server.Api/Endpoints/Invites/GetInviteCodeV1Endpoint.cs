using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Invites;

/// <summary>
/// The code of an invite, for the person who made it and nobody else.
/// </summary>
/// <remarks>
/// A code is a secret that admits whoever holds it. Showing every member every code would let anybody
/// who was ever in the repo walk back in with somebody else's.
/// </remarks>
public class GetInviteCodeV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/invites/{inviteId:guid}/code", GetInviteCode)
            .WithTags("Invites")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<InviteCodeDto>, BadRequest<CustomProblemDetails>>> GetInviteCode(
        Guid repoId, Guid inviteId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var invite = await dbContext.RepoInvites.GetAsync(new RepoInviteId(inviteId), cancellationToken);

        if (invite is null || invite.RepoId != new RepoId(repoId))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Invite '{inviteId}' does not exist"));
        }

        if (invite.CreatedBy != claimsPrincipal.GetUserId())
        {
            return TypedResults.BadRequest(Problems.InviteCodeNotYours);
        }

        return TypedResults.Ok(new InviteCodeDto(InviteCodes.Format(invite.Code)));
    }
}
