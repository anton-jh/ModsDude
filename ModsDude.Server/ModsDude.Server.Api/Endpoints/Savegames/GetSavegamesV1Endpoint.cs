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
/// The repo's savegames, each carrying its head version and whoever has it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One repo-level list, not a list per profile.</b> A savegame is keyed on the repo and only
/// points at a profile, so the faithful rendering is one list with a profile column - which is also
/// what stops two surfaces showing the same rows under different rules.
/// </para>
/// <para>
/// <b>The head and the claim ride along.</b> Every row needs both to say anything useful - where the
/// save stands and whether it can be taken - and a list of ten savegames must not be twenty
/// follow-up requests. Neither is it a query per row on this side: see
/// <see cref="SavegameReads"/> for the fixed number of round trips this costs whatever the repo
/// holds.
/// </para>
/// <para>
/// Guest, because reading who has a save is what decides whether to ask for it, and a member who
/// only ever downloads is exactly the person who needs to know.
/// </para>
/// </remarks>
public class GetSavegamesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/savegames", Get)
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

        // The clock is read once for the whole list, so that two rows cannot disagree about whether
        // the same instant had passed a claim's expiry.
        var savegames = await SavegameReads.GetListAsync(dbContext, new RepoId(repoId), timeService.Now(), cancellationToken);

        return TypedResults.Ok<IEnumerable<SavegameDto>>(savegames);
    }
}
