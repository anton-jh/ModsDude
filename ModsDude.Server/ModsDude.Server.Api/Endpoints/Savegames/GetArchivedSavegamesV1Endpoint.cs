using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// The repo's archived savegames, on the same Archive page as its archived profiles.
/// </summary>
/// <remarks>
/// Each row still carries its head version and whoever holds it, exactly as a live one does -
/// archiving changed the visibility, not the save. Somebody looking at the archive to decide
/// whether a save is safe to delete needs both of those to answer it.
/// </remarks>
public class GetArchivedSavegamesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/savegames/archived", Get)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok<IEnumerable<SavegameDto>>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Get(
        Guid repoId,
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

        var savegames = await SavegameReads.GetListAsync(
            dbContext, new RepoId(repoId), timeService.Now(), cancellationToken, archived: true);

        return TypedResults.Ok<IEnumerable<SavegameDto>>(savegames);
    }
}
