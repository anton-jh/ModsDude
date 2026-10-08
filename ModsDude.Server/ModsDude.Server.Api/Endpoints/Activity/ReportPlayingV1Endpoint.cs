using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Activity;

/// <summary>
/// Says whether one of the caller's games is being played right now: a heartbeat every minute while
/// it runs, and one stop when it closes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a game that follows a profile has a row to mark.</b> One that does not is on nothing a
/// friend could join, so the report is answered the same way and changes nothing.
/// </para>
/// <para>
/// Nothing to authorize beyond being signed in: it only ever touches the caller's own row, whose repo
/// was authorized when the row was recorded. Repeating a report gives the same outcome.
/// </para>
/// </remarks>
public class ReportPlayingV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("activity/playing", Report)
            .WithTags("Activity");
    }


    private static async Task<Ok> Report(
        ReportPlayingRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (await dbContext.GameActivities.GetAsync(claimsPrincipal.GetUserId(), new GameKey(request.Game), cancellationToken) is not GameActivity activity)
        {
            return TypedResults.Ok();
        }

        if (request.IsPlaying)
        {
            activity.SeenPlaying(timeService.Now());
        }
        else
        {
            activity.StoppedPlaying();
        }

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The caller's own clear deleted the row in the meantime: the game is on nothing, which is
            // the outcome a report about a game without a row has anyway.
        }

        return TypedResults.Ok();
    }


    /// <param name="Game">The game identity, as the client worked it out from the repo's adapter.</param>
    /// <param name="IsPlaying">True for a heartbeat, false when the game has closed.</param>
    public record ReportPlayingRequest(string Game, bool IsPlaying);
}
