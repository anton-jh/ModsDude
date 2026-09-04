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
/// Takes the claim on a savegame, or renews the caller's own.
/// </summary>
/// <remarks>
/// <para>
/// <b>One route for taking and for renewing</b>, because the client cannot tell the two apart
/// without asking first, and an answer it read a moment ago is exactly the thing that goes stale. It
/// sends the same request either way and the server decides which it was.
/// </para>
/// <para>
/// <b>Taking it from somebody is allowed.</b> That is this design's whole position on conflict: the
/// claim is the social half and refusing here would only teach people to check in junk versions to
/// free a save. The previous claim is closed as
/// <see cref="SavegameCheckoutEndReason.TakenOver"/> and returned in
/// <see cref="CheckOutSavegameResponse.TakenFrom"/>, so the client can say whose evening it just
/// interrupted and since when - a warning naming a person is the only kind anybody reads.
/// </para>
/// <para>
/// <b>Renewing a stale claim of one's own is not taking it over.</b> The holder coming back is
/// exactly the case staleness exists to describe, and making them take their own save off themselves
/// would put a <c>TakenOver</c> in the log for something nobody did.
/// </para>
/// <para>
/// The response carries no version. <b>Check-out always takes the head</b> - a restore copies
/// forward rather than moving the head back - so the version to write into the slot is the one the
/// savegame listing already gives.
/// </para>
/// </remarks>
public class CheckOutSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/savegames/{savegameId:guid}/checkouts", CheckOut)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok<CheckOutSavegameResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CheckOut(
        Guid repoId, Guid savegameId,
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

        var now = timeService.Now();
        var existing = await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(savegame.RepoId, savegame.Id, cancellationToken);

        SavegameCheckout checkout;
        SavegameCheckout? takenFrom = null;

        if (existing is not null && existing.UserId == userId)
        {
            // Still the caller's, however long ago it was taken. Pushing the expiry out is the whole
            // change, so no second row is opened and nothing about this reads as an event.
            existing.Renew(now);
            checkout = existing;
        }
        else
        {
            if (existing is not null)
            {
                existing.End(now, SavegameCheckoutEndReason.TakenOver);
                takenFrom = existing;
            }

            checkout = new SavegameCheckout(savegame.RepoId, savegame.Id, userId, now);
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

        return TypedResults.Ok(new CheckOutSavegameResponse(
            await SavegameReads.ToDtoAsync(dbContext, checkout, now, cancellationToken),
            takenFrom is null ? null : await SavegameReads.ToDtoAsync(dbContext, takenFrom, now, cancellationToken)));
    }


    /// <param name="Checkout">The caller's claim - freshly opened, or their own with its expiry pushed out.</param>
    /// <param name="TakenFrom">
    /// The claim this one closed, or <c>null</c> where nobody held the save. Carried so the client
    /// can name the person and the date it was taken on rather than saying only that somebody had
    /// it - which is the difference between a warning that means something and one people click
    /// past.
    /// </param>
    public record CheckOutSavegameResponse(SavegameCheckoutDto Checkout, SavegameCheckoutDto? TakenFrom);
}
