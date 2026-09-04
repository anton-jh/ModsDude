using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Who has had a savegame, and when, newest first.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the timeline the versions listing gives. Check-ins are already history - they
/// are versions - so only the taking half needs a log of its own, and
/// <see cref="SavegameVersionDto.CheckoutId"/> is what lets a client interleave the two into one
/// sequence rather than showing them as two lists.
/// </para>
/// <para>
/// <b><see cref="SavegameCheckoutDto.Status"/> is folded here, not in the client.</b> It is derived
/// from an expiry against a clock, and a client's clock is not the one the claim was written by -
/// so a save that reads as held on one machine would read as stale on another, which is exactly the
/// disagreement the claim exists to prevent.
/// </para>
/// </remarks>
public class GetSavegameCheckoutsV1Endpoint : IEndpoint
{
    private const int _defaultLimit = 50;
    private const int _maximumLimit = 200;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/savegames/{savegameId:guid}/checkouts", Get)
            .WithTags("Savegames");
    }


    /// <param name="skip">How many of the newest claims to pass over. An offset, as for the versions listing.</param>
    private static async Task<Results<Ok<GetSavegameCheckoutsResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Get(
        Guid repoId, Guid savegameId,
        int? skip, int? limit,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Guest))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var savegame = await dbContext.Savegames.GetAsync(new RepoId(repoId), new SavegameId(savegameId), cancellationToken);
        if (savegame is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No savegame '{savegameId}' found in repo '{repoId}'"));
        }

        var pageSize = Math.Clamp(limit ?? _defaultLimit, 1, _maximumLimit);
        var offset = Math.Max(skip ?? 0, 0);

        var rows = await dbContext.SavegameCheckouts.GetHistoryAsync(
            savegame.RepoId, savegame.Id, offset, pageSize, cancellationToken);

        var total = await dbContext.SavegameCheckouts.CountCheckoutsAsync(savegame.RepoId, savegame.Id, cancellationToken);

        var checkouts = await SavegameReads.ToDtosAsync(dbContext, rows, timeService.Now(), cancellationToken);

        return TypedResults.Ok(new GetSavegameCheckoutsResponse(
            checkouts,
            offset + checkouts.Count < total));
    }


    /// <param name="HasMore">
    /// Whether older claims remain. Claims arrive at the front, so a page read while somebody is
    /// taking the save can repeat a row - the same bargain the versions listing makes.
    /// </param>
    public record GetSavegameCheckoutsResponse(IEnumerable<SavegameCheckoutDto> Checkouts, bool HasMore);
}
