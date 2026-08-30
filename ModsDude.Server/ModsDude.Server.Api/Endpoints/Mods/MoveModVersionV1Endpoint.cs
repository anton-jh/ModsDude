using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <summary>
/// Moves an already-registered version to a new place in its mod's ordering. The backstop for an
/// order that is wrong for reasons optimistic concurrency cannot catch — a comparer that guessed
/// badly, or an arbitration someone regrets — and the mechanism a resolution dialog writes through.
/// </summary>
public class MoveModVersionV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/mods/{modId}/versions/{versionId}/placement", Move)
            .WithTags("Mods");
    }


    /// <summary>
    /// The placement names the version's new neighbours <b>in the ordering without it</b>, and both
    /// are asserted, exactly as at registration and for the same reason: relative placement against
    /// one neighbour stops collisions but still permits a silently wrong order when two members act
    /// on a state neither has seen the other change, which offers a downgrade as an upgrade. A
    /// violation is a rejection rather than something to repair — but unlike an import, which
    /// recomputes and retries, a hand-authored order is a human's answer to a question the server
    /// cannot re-answer, so the client refetches and asks again.
    /// </summary>
    private static async Task<Results<Ok<MoveModVersionResponse>, BadRequest<CustomProblemDetails>>> Move(
        Guid repoId, string modId, string versionId,
        MoveModVersionRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var siblings = await dbContext.ModVersions.GetVersionsOfModAsync(new RepoId(repoId), new ModId(modId), cancellationToken);

        var modVersion = siblings.FirstOrDefault(x => x.Id == new ModVersionId(versionId));
        if (modVersion is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No version '{versionId}' of mod '{modId}' found in repo '{repoId}'"));
        }

        var after = request.Placement.After is null ? (ModVersionId?)null : new ModVersionId(request.Placement.After);
        var before = request.Placement.Before is null ? (ModVersionId?)null : new ModVersionId(request.Placement.Before);

        if (!ModVersionSequencer.CheckMoveIsValid(siblings, modVersion, after, before))
        {
            return TypedResults.BadRequest(Problems.VersionPlacementConflict(new RepoId(repoId), new ModId(modId)));
        }

        if (ModVersionSequencer.CheckMoveChangesTheOrder(siblings, modVersion, after, before))
        {
            await ApplyMoveAsync(dbContext, unitOfWork, siblings, modVersion, after, before, timeService.Now(), cancellationToken);
        }

        return TypedResults.Ok(new MoveModVersionResponse(siblings
            .OrderBy(x => x.SequenceNumber)
            .Select(x => x.Id.Value)));
    }


    /// <summary>
    /// Two writes, because the ordering cannot get from one contiguous state to the other in a
    /// single one: a move is a rotation, and no order of row writes takes a rotation through a
    /// unique index without two rows briefly sharing a sequence number.
    /// <see cref="ModVersionSequencer.VacateForMove"/> explains the shape. The transaction is what
    /// makes the halfway state — where the version is parked past the end and the ordering has a
    /// hole — something no other request and no crash can ever observe.
    /// </summary>
    private static async Task ApplyMoveAsync(
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<ModVersion> siblings,
        ModVersion modVersion,
        ModVersionId? after,
        ModVersionId? before,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        ModVersionSequencer.VacateForMove(siblings, modVersion, timestamp);
        await unitOfWork.CommitAsync(cancellationToken);

        ModVersionSequencer.MoveTo(siblings, modVersion, after, before, timestamp);
        await unitOfWork.CommitAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }


    public record MoveModVersionRequest(ModVersionPlacement Placement);

    /// <summary>
    /// The resulting order, oldest first. Returned because rewriting a hand-authored order takes one
    /// move per version that actually shifted, and each of those placements has to be computed
    /// against the order the previous move left behind.
    /// </summary>
    public record MoveModVersionResponse(IEnumerable<string> VersionIdsInOrder);
}
