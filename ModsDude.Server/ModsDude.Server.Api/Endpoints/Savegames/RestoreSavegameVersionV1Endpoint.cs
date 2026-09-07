using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Puts an older version of a savegame back, by copying it to the front as a new one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is deleted and the head never moves backwards.</b> Restoring version 4 while the head
/// is 12 produces version 13 naming the same bytes. Moving the head back to 4 instead would strand
/// 5 to 12 as a future nobody can reach, and would put a client that is holding one of them on a
/// version the server says is ahead of its own head.
/// </para>
/// <para>
/// So <b>check-out always takes the head</b>. There is no stale base to reason about at the moment
/// somebody wants to play, which is the whole reason this is copy-forward rather than a pointer
/// somebody moves.
/// </para>
/// <para>
/// <b>No bytes move.</b> A version is addressed by content, so the restored version names the hash
/// it was copied from and the blob it points at is already there - which is what makes restoring a
/// 400 MB save a metadata write. It carries the source's <c>ProfileId</c> and
/// <c>ProfileRevision</c> too, because those describe the play in the file rather than the moment
/// the restore was clicked.
/// </para>
/// <para>
/// Member, like any other check-in. It discards nothing, and the history makes it visible and
/// reversible by another restore - a better guarantee than a permission level. The claim is left
/// alone: restoring is not taking the save, and whoever is holding it finds out the same way they
/// would from any other check-in, by their base no longer being the head.
/// </para>
/// </remarks>
public class RestoreSavegameVersionV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/savegames/{savegameId:guid}/versions/{number:int}/restore", Restore)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok<SavegameVersionDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Restore(
        Guid repoId, Guid savegameId, int number,
        RestoreSavegameVersionRequest? request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();

        var authResult = await dbContext.Users.GetAsync(userId, cancellationToken)
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

        var sourceNumber = new SavegameVersionNumber(number);

        // Absent rather than merely old: a pruned version's number stays out of the sequence for
        // good, so this is the ordinary answer for anything far enough back and not the sign of a
        // bad request.
        var source = await dbContext.SavegameVersions.GetRowAsync(savegame.RepoId, savegame.Id, sourceNumber, cancellationToken);
        if (source is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Savegame '{savegameId}' has no version {number}"));
        }

        // The blob is not checked for. Its address is already named by a version that is registered,
        // so anything that would fail here has failed for the source too - and a restore is the one
        // thing that can still be useful when a blob has gone: it moves nothing.
        var version = savegame.CreateVersion(
            source.ProfileId,
            source.ProfileRevision,
            source.ContentHash,
            source.SizeBytes,
            userId,
            timeService.Now(),
            // The source's own label is not copied. A label is the note somebody wrote on the
            // version they named, and duplicating it would leave two rows claiming to be the one
            // that was kept.
            request?.Label,
            SavegameVersionOrigin.Restored,
            sourceNumber,
            // Copied forward with the bytes. A restore is the same save, so the map it was played on
            // and the hours in it are the same facts - re-deriving them is impossible here anyway,
            // since the server has never looked inside a savegame.
            details: source.Details);

        dbContext.SavegameVersions.Add(version);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Somebody checked in against the head this restore also computed a successor to, and
            // the primary key let one of them through. Reported as staleness because that is what it
            // is: the restore was built on a head that has moved.
            return TypedResults.BadRequest(Problems.SavegameVersionStale(savegame.Id, sourceNumber, version.Number));
        }

        // A restore mints a version like any other, so it can push the oldest one over the limit.
        // The version it copied forward is safe from that by being recent, and its bytes are safe
        // regardless: the new head names the same blob.
        await SavegamePruning.PruneAsync(dbContext, savegame, cancellationToken);

        return TypedResults.Ok(await SavegameReads.ToDtoAsync(dbContext, version, cancellationToken));
    }


    /// <summary>
    /// A restore is recorded whether or not it changes anything - unlike a check-in, which mints
    /// nothing when the bytes are unchanged. Restoring the version that is already the head is
    /// somebody asking for it explicitly, and a history that quietly did nothing would read as a bug.
    /// </summary>
    public record RestoreSavegameVersionRequest(string? Label);
}
