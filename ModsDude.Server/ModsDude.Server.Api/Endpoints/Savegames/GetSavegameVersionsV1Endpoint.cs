using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// A savegame's history, newest first.
/// </summary>
/// <remarks>
/// Readable at Guest, like a profile's history and for the same reason: somebody who only ever takes
/// a copy of a save still needs to know what happened to it, and which version to ask to have back
/// when the current one turns out to be broken.
/// </remarks>
public class GetSavegameVersionsV1Endpoint : IEndpoint
{
    private const int _defaultLimit = 50;
    private const int _maximumLimit = 200;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/savegames/{savegameId:guid}/versions", Get)
            .WithTags("Savegames");
    }


    /// <param name="skip">How many of the newest versions to pass over. See the response for why this is an offset.</param>
    private static async Task<Results<Ok<GetSavegameVersionsResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Get(
        Guid repoId, Guid savegameId,
        int? skip, int? limit,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
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

        var rows = await dbContext.SavegameVersions.GetHistoryAsync(
            savegame.RepoId, savegame.Id, offset, pageSize, cancellationToken);

        // Counted rather than inferred from the head. Version numbers are not contiguous - pruning
        // leaves the gap where an old version was - so the head says nothing about how many rows are
        // behind it, unlike a profile's revision number.
        var total = await dbContext.SavegameVersions.CountVersionsAsync(savegame.RepoId, savegame.Id, cancellationToken);

        var versions = await SavegameReads.ToDtosAsync(dbContext, savegame.RepoId, rows, cancellationToken);

        return TypedResults.Ok(new GetSavegameVersionsResponse(
            versions,
            savegame.HeadVersion.Value,
            offset + versions.Count < total));
    }


    /// <param name="HeadVersion">
    /// Which of them is current. Carried so that a history page does not have to infer "this is the
    /// live one" from the first row of a listing it may have paged into - and it could not infer it
    /// from the highest number either, since a restore makes the newest row the head while an older
    /// number is what it was copied from.
    /// </param>
    /// <param name="HasMore">
    /// Whether older versions remain. The window is an offset from the newest, because
    /// <see cref="SavegameVersionNumber"/> is a value object and a provider cannot translate a
    /// comparison on one - so a page read while somebody is checking in can repeat a row.
    /// </param>
    public record GetSavegameVersionsResponse(IEnumerable<SavegameVersionDto> Versions, int HeadVersion, bool HasMore);
}
