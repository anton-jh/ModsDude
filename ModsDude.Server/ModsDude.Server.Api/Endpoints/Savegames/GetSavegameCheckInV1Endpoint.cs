using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// What became of a check-in whose answer never reached the client, without sending it again.
/// </summary>
/// <remarks>
/// A client that has played on since cannot repeat the check-in - the slot no longer holds those bytes
/// - so it asks instead: answered as the check-in was, or <see cref="Problems.SavegameCheckInUnknown"/>
/// where it is not the caller's latest recorded check-in of the savegame. Member, like checking in.
/// </remarks>
public class GetSavegameCheckInV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/savegames/{savegameId:guid}/check-ins/{requestId:guid}", Get)
            .WithTags("Savegames")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<CheckInSavegameV1Endpoint.CheckInSavegameResponse>, BadRequest<CustomProblemDetails>>> Get(
        Guid repoId, Guid savegameId, Guid requestId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var savegame = await dbContext.Savegames.GetAsync(new RepoId(repoId), new SavegameId(savegameId), cancellationToken);
        if (savegame is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No savegame '{savegameId}' found in repo '{repoId}'"));
        }

        var userId = claimsPrincipal.GetUserId();

        var recorded = await dbContext.SavegameSnapshotRequests.AsNoTracking().FirstOrDefaultAsync(
            x => x.RepoId == savegame.RepoId && x.SavegameId == savegame.Id && x.UserId == userId, cancellationToken);

        if (recorded is null || recorded.Answers(new SavegameSnapshotRequestId(requestId)) is false)
        {
            return TypedResults.BadRequest(Problems.SavegameCheckInUnknown(savegame.Id, requestId));
        }

        return await CheckInSavegameV1Endpoint.AnswerAgainAsync(dbContext, recorded, cancellationToken);
    }
}
