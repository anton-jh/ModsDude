using Microsoft.AspNetCore.Http.HttpResults;
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
/// Renames a savegame. That is the whole of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no operation that moves a savegame to another profile</b>, and this used to be it.
/// Moving one would put <see cref="Savegame.ProfileId"/> and every snapshot's
/// <see cref="SavegameSnapshot.ProfileId"/> in disagreement, and the snapshots are the honest half -
/// they name the mod lists that actually produced those bytes. It would also make a save's target
/// revision incomparable with its own history, since revision numbers of two profiles have nothing
/// to do with each other.
/// </para>
/// <para>
/// A person who wants the effect republishes the savegame onto the profile they want, which is three
/// operations that already exist: check it out, discard it - handing the claim back and clearing the
/// binding - and publish that slot as a new savegame with its own history. The original stays where
/// it is, intact. See docs/10-savegame-profile-binding.md#cardinality.
/// </para>
/// </remarks>
public class UpdateSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/savegames/{savegameId:guid}", Update)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok<SavegameDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Update(
        Guid repoId, Guid savegameId,
        UpdateSavegameRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
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

        // The overload that excludes this savegame, so that saving the row unchanged - which is what
        // renaming something to what it is already called does - is not refused as a clash with
        // itself.
        if (await dbContext.Savegames.CheckNameIsTaken(new RepoId(repoId), savegame.Id, new SavegameName(request.Name), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(request.Name));
        }

        savegame.Name = new SavegameName(request.Name);

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(await SavegameReads.DescribeAsync(dbContext, savegame, timeService.Now(), cancellationToken));
    }


    public record UpdateSavegameRequest(string Name);
}
