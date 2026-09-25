using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Activity;

/// <summary>
/// Says which profile one of the caller's games is now on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Called by the client after the fact, and never waited on.</b> The activation already happened on
/// that machine; this only lets the caller's friends see it. A report that never arrives leaves them
/// looking at the last one, and the next activation or re-apply puts it right.
/// </para>
/// <para>
/// <b>Guest is enough.</b> A guest can read a repo's profiles and so can activate one, and whoever
/// they share the repo with can see what they are playing either way.
/// </para>
/// </remarks>
public class RecordGameActivityV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("activity", Record)
            .WithTags("Activity");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Record(
        RecordGameActivityRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();
        var repoId = new RepoId(request.RepoId);

        var authResult = await dbContext.Users.GetAsync(userId, cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(repoId, RepoMembershipLevel.Guest))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var profileId = new ProfileId(request.ProfileId);

        if (await dbContext.Profiles.GetAsync(repoId, profileId, cancellationToken) is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{request.ProfileId}' found in repo '{request.RepoId}'"));
        }

        var game = new GameKey(request.Game);
        var pinned = request.PinnedRevision is int revision ? new RevisionNumber(revision) : (RevisionNumber?)null;
        var savegameId = request.SavegameId is Guid id ? new SavegameId(id) : (SavegameId?)null;
        var now = timeService.Now();

        if (await dbContext.GameActivities.GetAsync(userId, game, cancellationToken) is GameActivity existing)
        {
            existing.Record(repoId, profileId, pinned, request.Kind, savegameId, now);
        }
        else
        {
            dbContext.GameActivities.Add(new GameActivity(userId, game, repoId, profileId, pinned, request.Kind, savegameId, now));
        }

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two of the caller's own reports about one game arriving in the same instant, and the
            // other one made the row. Either of them describes the game as well as the other.
        }

        return TypedResults.Ok();
    }


    /// <param name="Game">The game identity, as the client worked it out from the repo's adapter.</param>
    /// <param name="PinnedRevision">
    /// The revision the game is held to, or null where it follows head - which an ordinary activation
    /// always does, whatever number head happens to be.
    /// </param>
    /// <param name="SavegameId">The savegame checked out, where <paramref name="Kind"/> is a check-out.</param>
    public record RecordGameActivityRequest(
        string Game,
        Guid RepoId,
        Guid ProfileId,
        int? PinnedRevision,
        GameActivityKind Kind,
        Guid? SavegameId);
}
