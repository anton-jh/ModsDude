using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Retention;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Takes the claim on a savegame, or renews the caller's own.
/// </summary>
/// <remarks>
/// <para>
/// <b>One route for taking and for renewing</b>, because the client cannot tell the two apart
/// without asking first, and an answer it read a moment ago is exactly the thing that goes stale. It
/// sends the same request either way and the server decides which it was.
/// </para>
/// <para>
/// <b>Taking it from somebody is allowed - knowingly.</b> That is this design's whole position on
/// conflict: the claim is the social half and refusing here would only teach people to check in junk
/// snapshots to free a save. The request says which claim the caller saw, and a different one is
/// refused, so nobody's evening is interrupted on the strength of a list that did not show them. The previous claim is closed as
/// <see cref="SavegameCheckoutEndReason.TakenOver"/> and returned in
/// <see cref="CheckOutSavegameResponse.TakenFrom"/>, so the client can say whose evening it just
/// interrupted and since when - a warning naming a person is the only kind anybody reads.
/// </para>
/// <para>
/// <b>Taking one's own claim again is not taking it over.</b> The holder coming back is answered with
/// the claim they already have, unchanged - making them take their own save off themselves would put a
/// <c>TakenOver</c> in the log for something nobody did.
/// </para>
/// <para>
/// <b>Check-out always takes the head</b> - a restore copies forward rather than moving the head
/// back. The request says which head the caller saw, a different one is refused, and the response
/// carries the head the claim was granted on: that is the snapshot to write into the slot.
/// </para>
/// </remarks>
public class CheckOutSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/savegames/{savegameId:guid}/checkouts", CheckOut)
            .WithTags("Savegames")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<CheckOutSavegameResponse>, BadRequest<CustomProblemDetails>>> CheckOut(
        Guid repoId, Guid savegameId,
        CheckOutSavegameRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        IRetentionUpkeep retentionUpkeep,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();

        var savegame = await dbContext.Savegames.GetAsync(new RepoId(repoId), new SavegameId(savegameId), cancellationToken);
        if (savegame is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No savegame '{savegameId}' found in repo '{repoId}'"));
        }

        var now = timeService.Now();
        var existing = await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(savegame.RepoId, savegame.Id, cancellationToken);

        if (savegame.HeadSnapshot.Value != request.ExpectedHead)
        {
            return TypedResults.BadRequest(Problems.SavegameHeadMoved(savegame.Id, request.ExpectedHead, savegame.HeadSnapshot));
        }

        if (existing is not null && existing.UserId != userId && existing.Id.Value != request.ExpectedCheckoutId)
        {
            return TypedResults.BadRequest(Problems.SavegameClaimChanged(savegame.Id, await SavegameReads.ToDtoAsync(dbContext, existing, cancellationToken)));
        }

        SavegameCheckout checkout;
        SavegameCheckout? takenFrom = null;
        var head = await dbContext.SavegameSnapshots.GetRowAsync(savegame.RepoId, savegame.Id, savegame.HeadSnapshot, cancellationToken);

        if (head is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Savegame '{savegameId}' has no snapshot to check out yet."));
        }

        if (existing is not null && existing.UserId == userId)
        {
            // Still the caller's, however long ago it was taken: nothing changes, no second row is opened
            // and nothing about this reads as an event.
            checkout = existing;
        }
        else
        {
            if (existing is not null)
            {
                existing.End(now, SavegameCheckoutEndReason.TakenOver);
                takenFrom = existing;
            }

            // The head is what is about to be written into the slot, so its revision is where this
            // claim's play starts - see SavegameCheckout.HoldsFromRevision.
            checkout = new SavegameCheckout(savegame.RepoId, savegame.Id, userId, now, savegame.ProfileId is null ? null : head.ProfileRevision);
            dbContext.SavegameCheckouts.Add(checkout);
        }

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two people took the save in the same instant and the one-open-claim index let exactly
            // one through. Taking a save from somebody is allowed; taking it from two people at once
            // is not a state the log can represent, so the loser is told to look again - what they
            // would see has changed since they decided.
            return TypedResults.BadRequest(Problems.SavegameCheckoutConflict(savegame.Id));
        }

        if (savegame.ProfileId is ProfileId profileId && checkout != existing)
        {
            // The claim now holds revisions, so any of them shown as due to go stops saying so.
            await retentionUpkeep.ReleaseProfileAsync(savegame.RepoId, profileId, cancellationToken);
        }

        return TypedResults.Ok(new CheckOutSavegameResponse(
            await SavegameReads.ToDtoAsync(dbContext, checkout, cancellationToken),
            takenFrom is null ? null : await SavegameReads.ToDtoAsync(dbContext, takenFrom, cancellationToken),
            await SavegameReads.ToDtoAsync(dbContext, savegame.RepoId, head, cancellationToken)));
    }


    /// <param name="Checkout">The caller's claim - freshly opened, or the one of their own they already held.</param>
    /// <param name="TakenFrom">
    /// The claim this one closed, or <c>null</c> where nobody held the save. Carried so the client
    /// can name the person and the date it was taken on rather than saying only that somebody had
    /// it - which is the difference between a warning that means something and one people click
    /// past.
    /// </param>
    /// <param name="Head">The snapshot the claim was granted on, which is the one to write into the slot.</param>
    public record CheckOutSavegameResponse(SavegameCheckoutDto Checkout, SavegameCheckoutDto? TakenFrom, SavegameSnapshotDto Head);

    /// <param name="ExpectedCheckoutId">
    /// The open claim the caller saw, or null where it saw none. Somebody else's claim other than this one
    /// is refused rather than taken: the caller agreed to take the one it saw.
    /// </param>
    /// <param name="ExpectedHead">The head snapshot the caller saw. A different head is refused.</param>
    public record CheckOutSavegameRequest(Guid? ExpectedCheckoutId, int ExpectedHead);
}
