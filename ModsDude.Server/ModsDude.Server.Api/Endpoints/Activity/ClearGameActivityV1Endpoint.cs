using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Activity;

/// <summary>
/// Says that one of the caller's games no longer follows a profile.
/// </summary>
/// <remarks>
/// The row goes rather than being marked: a game on nothing is not something anybody's list has a
/// line for. Nothing to authorize beyond being signed in - it only ever removes the caller's own row,
/// and removing one that is not there is the same answer.
/// </remarks>
public class ClearGameActivityV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("activity", Clear)
            .WithTags("Activity");
    }


    /// <param name="game">
    /// The game identity. A query parameter rather than part of the path, because an identity may
    /// hold a '#' - which a path cannot carry.
    /// </param>
    private static async Task<Ok> Clear(
        string game,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (await dbContext.GameActivities.GetAsync(claimsPrincipal.GetUserId(), new GameKey(game), cancellationToken) is GameActivity existing)
        {
            dbContext.GameActivities.Remove(existing);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok();
    }
}
