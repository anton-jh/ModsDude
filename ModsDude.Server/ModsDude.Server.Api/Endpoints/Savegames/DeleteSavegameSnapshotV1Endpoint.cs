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
using ModsDude.Server.Persistence.Retention;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Deletes one snapshot of a savegame's history.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reason this exists is the other direction.</b> A savegame snapshot pins the profile
/// revision it was played on, so it is what stops that revision being pruned. Without a way to
/// remove one, "this revision was played on save X snapshot 3" would be a refusal with nothing behind
/// it - the user could see the obstacle and never move it.
/// </para>
/// <para>
/// <b>Admin only</b>, like pruning revisions and for the same reason: it destroys a backup, which is
/// not part of running a repo.
/// </para>
/// <para>
/// <b>The head is refused.</b> It is the snapshot a check-out hands people, and a savegame whose
/// current snapshot is missing is a savegame nobody can play. Deleting the whole savegame is a
/// different act with its own endpoint.
/// </para>
/// <para>
/// <b>Rows only.</b> Several snapshots legitimately share one blob - the address is the content hash
/// - so the bytes are left to the reclamation sweep, which asks whether anything still refers to the
/// address. Deleting them here would mean re-asking that in this transaction and destroying
/// somebody's save when the answer came out wrong. Same bargain as <see cref="RetentionSweeper"/>.
/// </para>
/// </remarks>
public class DeleteSavegameSnapshotV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/savegames/{savegameId:guid}/snapshots/{number:int}", Delete)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Delete(
        Guid repoId, Guid savegameId, int number,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        RetentionUpkeep retentionUpkeep,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Admin))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var savegame = await dbContext.Savegames.GetAsync(new RepoId(repoId), new SavegameId(savegameId), cancellationToken);
        if (savegame is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Savegame '{savegameId}' does not exist in repo '{repoId}'"));
        }

        var snapshot = new SavegameSnapshotNumber(number);

        if (snapshot == savegame.HeadSnapshot)
        {
            return TypedResults.BadRequest(Problems.CannotDeleteHeadSavegameSnapshot(new SavegameId(savegameId), snapshot));
        }

        var deleted = await dbContext.SavegameSnapshots.DeleteSnapshotsAsync(
            new RepoId(repoId), savegame.Id, [snapshot], cancellationToken);

        if (deleted == 0)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Savegame '{savegameId}' has no snapshot {number}"));
        }

        await unitOfWork.CommitAsync(cancellationToken);

        // One snapshot fewer can turn a history that was outside its window into one winding down.
        await retentionUpkeep.ReleaseSavegameAsync(savegame.RepoId, savegame.Id, cancellationToken);

        return TypedResults.Ok();
    }
}
