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
/// Puts a savegame away.
/// </summary>
/// <remarks>
/// <para>
/// <b>The only way to make a savegame go away</b>, and deliberately not a delete: what it carries is
/// backups of somebody's play, and there is no getting those back. Permanent deletion is a second
/// act, taken from the archive.
/// </para>
/// <para>
/// <b>Member</b>, like publishing and checking in - archiving is reversible and is part of keeping a
/// repo's saves tidy. Permanently deleting one is Admin.
/// </para>
/// <para>
/// <b>The claim log is left exactly as it was.</b> Archiving a save somebody is holding must not
/// quietly release their hold on it - if they finish playing and check in, the version should land
/// on the savegame they took, archived or not. Only visibility and the name change.
/// </para>
/// </remarks>
public class ArchiveSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/savegames/{savegameId:guid}/archive", Archive)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Archive(
        Guid repoId, Guid savegameId,
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
            return TypedResults.BadRequest(Problems.NotFound);
        }

        savegame.Archive(timeService.Now());

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
