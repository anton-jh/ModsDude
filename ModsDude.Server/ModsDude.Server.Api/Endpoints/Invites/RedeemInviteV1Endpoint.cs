using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Invites;

/// <summary>
/// Joins the caller to whichever repo the code belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The one endpoint in the system that admits somebody to a repo, and it is the person joining who
/// calls it. That is the whole difference from looking a stranger up by name and adding them: a user
/// is reachable only through a code they were given, so a common display name is not a way to find
/// anybody, and nobody is put anywhere they did not ask to be.
/// </para>
/// <para>
/// The code arrives in the body rather than the path because it is a secret, and a path is written
/// down by every proxy and access log between here and the client.
/// </para>
/// </remarks>
public class RedeemInviteV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("invites/redeem", RedeemInvite)
            .WithTags("Invites");
    }


    private async Task<Results<Ok<RepoMembershipDto>, BadRequest<CustomProblemDetails>>> RedeemInvite(
        RedeemInviteRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        ITimeService timeService,
        CancellationToken cancellationToken)
    {
        if (!InviteCodes.TryParse(request.Code, out var code))
        {
            return TypedResults.BadRequest(Problems.InviteNotFound.With(
                x => x.Detail = "That is not an invite code. Check it against the one you were sent."));
        }

        var invite = await dbContext.RepoInvites.GetByCodeAsync(code, cancellationToken);
        if (invite is null)
        {
            return TypedResults.BadRequest(Problems.InviteNotFound);
        }

        var now = timeService.Now();
        var status = invite.GetStatus(now);
        if (status is not InviteStatus.Active)
        {
            return TypedResults.BadRequest(Problems.InviteNotUsable(status));
        }

        var repo = await dbContext.Repos.GetAsync(invite.RepoId, cancellationToken);
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.InviteNotFound);
        }

        var userId = claimsPrincipal.GetUserId();

        // Already in, so the code has done its job and must not be charged a use for saying so
        // again. A second click, or a link redeemed twice, is not an error to the person clicking.
        if (repo.GetMembership(userId) is RepoMembership existing)
        {
            return TypedResults.Ok(new RepoMembershipDto(RepoDto.FromModel(repo), existing.Level));
        }

        repo.AddMember(userId, invite.GrantedLevel);
        invite.Redeem(now);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The invite moved under this request - somebody else redeemed or revoked it between the
            // read and the write. Which of those it was is only knowable by looking again, so the
            // caller is told to do exactly that.
            return TypedResults.BadRequest(Problems.InviteRedemptionConflict);
        }

        return TypedResults.Ok(new RepoMembershipDto(RepoDto.FromModel(repo), invite.GrantedLevel));
    }


    public record RedeemInviteRequest(string Code);
}
