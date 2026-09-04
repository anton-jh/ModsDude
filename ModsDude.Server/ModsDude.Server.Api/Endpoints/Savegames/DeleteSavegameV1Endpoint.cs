using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Deletes a savegame, its whole history and its whole log of claims.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one destructive operation in this aggregate</b>, and deliberately the only one: a
/// check-in supersedes, a restore copies forward, and pruning keeps the head and everything
/// labelled. Deleting a savegame is somebody saying the whole thing is finished with, so the
/// versions and the claims go with it - a claim log for a savegame that no longer exists is not a
/// record of anything.
/// </para>
/// <para>
/// <b>It does not refuse over an open claim.</b> Somebody holding a save that the group has decided
/// to delete is a conversation, not a constraint, and a delete that could be blocked by a claim
/// nobody has renewed since March would be blocked for good.
/// </para>
/// <para>
/// The blobs are not deleted here. They are addressed by content and several versions can name one,
/// so the reclamation sweep is what removes them once nothing refers to them - the same bargain as
/// a deleted mod's file.
/// </para>
/// </remarks>
public class DeleteSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/savegames/{savegameId:guid}", Delete)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Delete(
        Guid repoId, Guid savegameId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
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

        // The versions and the claims go with it by cascade, in the database rather than here, so
        // that a savegame cannot be left half-deleted by a request that stopped between two loops.
        dbContext.Savegames.Remove(savegame);

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
