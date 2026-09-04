using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
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
/// Gives a savegame back without checking anything in.
/// </summary>
/// <remarks>
/// <para>
/// <b>It mints no version</b>, which is the entire point. A save taken by mistake and never played
/// has nothing to record, and without this route the only ways out are a junk version in the history
/// or waiting to be taken over - the first lies about what happened and the second leaves the save
/// looking held all evening.
/// </para>
/// <para>
/// <b>Only the holder may discard.</b> <see cref="SavegameCheckoutEndReason.Discarded"/> means "the
/// person who had it gave it back unplayed", and letting somebody else write that would put a
/// sentence in the log that its subject never said. Taking a save off somebody is allowed and is
/// what the check-out route is for - it records
/// <see cref="SavegameCheckoutEndReason.TakenOver"/>, which is what actually happened.
/// </para>
/// </remarks>
public class DiscardSavegameCheckoutV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/savegames/{savegameId:guid}/checkouts/current", Discard)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Discard(
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

        var checkout = await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(savegame.RepoId, savegame.Id, cancellationToken);

        // Somebody else's open claim is answered the same way as no claim at all: the caller has
        // nothing here to give back, which is what the problem says. Their route to that save is
        // taking it, and taking is a decision that should be made on a screen showing whose it is
        // rather than fallen into by asking to discard.
        if (checkout is null || checkout.UserId != userId)
        {
            return TypedResults.BadRequest(Problems.SavegameNotCheckedOut(savegame.Id));
        }

        checkout.End(timeService.Now(), SavegameCheckoutEndReason.Discarded);

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
