using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Invites;

/// <summary>
/// Makes the caller trusted. Open to every signed-in user: the code is the authorization.
/// </summary>
/// <remarks>
/// The code arrives in the body rather than the path because it is a secret, and a path is written
/// down by every proxy and access log on the way.
/// </remarks>
public class RedeemTrustCodeV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("trust-codes/redeem", RedeemTrustCode)
            .WithTags("TrustCodes");
    }


    private async Task<Results<Ok<CurrentUserDto>, BadRequest<CustomProblemDetails>>> RedeemTrustCode(
        RedeemTrustCodeRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        ITimeService timeService,
        ILogger<RedeemTrustCodeV1Endpoint> logger,
        CancellationToken cancellationToken)
    {
        if (!InviteCodes.TryParse(request.Code, out var code))
        {
            return TypedResults.BadRequest(Problems.TrustCodeNotFound.With(
                x => x.Detail = "That is not a trust code. Check it against the one you were sent."));
        }

        var trustCode = await dbContext.TrustCodes.GetByCodeAsync(code, cancellationToken);
        if (trustCode is null)
        {
            return TypedResults.BadRequest(Problems.TrustCodeNotFound);
        }

        var userId = claimsPrincipal.GetUserId();
        var user = await dbContext.Users.GetAsync(userId, cancellationToken)
            ?? throw new NotAuthenticatedException();

        // Covers a repeat of this user's own redemption too. Either way the code has nothing to
        // grant, so it is not used up.
        if (user.IsTrusted)
        {
            return TypedResults.Ok(CurrentUserDto.FromModel(user));
        }

        var now = timeService.Now();
        var status = trustCode.GetStatus(now);
        if (status is not TrustCodeStatus.Active)
        {
            return TypedResults.BadRequest(Problems.TrustCodeNotUsable(status));
        }

        trustCode.Redeem(user, now);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogInformation(exception, "Redeeming trust code {TrustCodeId} for user {UserId} lost to a concurrent write.", trustCode.Id.Value, userId.Value);

            // The code was used or revoked under this request, or this user redeemed another code
            // at the same moment. Only the second leaves them trusted.
            var current = await dbContext.Users.AsNoTracking().FirstAsync(x => x.Id == userId, cancellationToken);
            if (current.IsTrusted)
            {
                return TypedResults.Ok(CurrentUserDto.FromModel(current));
            }

            return TypedResults.BadRequest(Problems.TrustCodeRedemptionConflict);
        }

        return TypedResults.Ok(CurrentUserDto.FromModel(user));
    }


    public record RedeemTrustCodeRequest(string Code);
}
