using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
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
/// Mints a code that lets whoever holds it join this repo.
/// </summary>
/// <remarks>
/// Gated the same way handing out a membership directly used to be: Member to invite at all, and
/// never above the inviter's own level. The invite is the grant, made ahead of time - so it has to
/// be refused now for anything it could not be allowed to do later.
/// </remarks>
public class CreateInviteV1Endpoint : IEndpoint
{
    /// <summary>The unique index makes a repeat impossible rather than unlikely; this is for the unlikely.</summary>
    private const int _maximumCodeAttempts = 3;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/invites", CreateInvite)
            .WithTags("Invites");
    }


    private async Task<Results<Ok<RepoInviteDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CreateInvite(
        Guid repoId, CreateInviteRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        ITimeService timeService,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member)
                .GrantAccessToRepo(new RepoId(repoId), request.MembershipLevel))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var now = timeService.Now();

        // Checked after authorization so that an admin asking for it is told why rather than being
        // told they may not - they may, and that is exactly the point: this is a rule about codes,
        // not about them.
        if (request.MembershipLevel >= RepoMembershipLevel.Admin)
        {
            return TypedResults.BadRequest(Problems.InviteCannotGrantAdmin);
        }

        if (request.MaximumUses is <= 0)
        {
            return TypedResults.BadRequest(Problems.InvalidInviteLimits(
                $"An invite cannot be limited to {request.MaximumUses} uses."));
        }

        if (request.ExpiresAt is DateTime expiry && expiry <= now)
        {
            return TypedResults.BadRequest(Problems.InvalidInviteLimits(
                "An invite cannot expire in the past."));
        }

        for (var attempt = 1; ; attempt++)
        {
            var invite = new RepoInvite(
                new RepoId(repoId),
                InviteCodes.Generate(),
                request.MembershipLevel,
                claimsPrincipal.GetUserId(),
                now,
                request.ExpiresAt,
                request.MaximumUses);

            dbContext.RepoInvites.Add(invite);

            try
            {
                await unitOfWork.CommitAsync(cancellationToken);

                return TypedResults.Ok(RepoInviteDto.FromModel(invite, now));
            }
            catch (DbUpdateException) when (attempt < _maximumCodeAttempts)
            {
                dbContext.Entry(invite).State = EntityState.Detached;
            }
        }
    }


    public record CreateInviteRequest(RepoMembershipLevel MembershipLevel, int? MaximumUses, DateTime? ExpiresAt);
}
