using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
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
/// Brings an archived savegame back into the repo's list, optionally under a new name.
/// </summary>
/// <remarks>
/// Named "unarchive" in the route rather than "restore", because a savegame already has a restore
/// and it means something else entirely: putting an old <em>version</em> back. Two things called
/// restore, one aggregate apart, is how somebody ends up rolling a save back a month by accident.
/// </remarks>
public class RestoreSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/savegames/{savegameId:guid}/unarchive", Unarchive)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Unarchive(
        Guid repoId, Guid savegameId,
        RestoreRequest? request,
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
            return TypedResults.BadRequest(Problems.NotFound);
        }

        SavegameName? name = request?.Name is { Length: > 0 } requested
            ? new SavegameName(requested)
            : null;

        var wanted = name ?? savegame.Name;

        // Where the clash deferred by archiving is finally resolved. The filtered unique index says
        // the same thing underneath; this is what turns it into a message.
        if (await dbContext.Savegames.CheckNameIsTaken(new RepoId(repoId), savegame.Id, wanted, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(wanted.Value));
        }

        savegame.Restore(name);

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
