using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <summary>
/// Removing a mod outright, which the per-version delete cannot express because it refuses the last
/// remaining version. This is also what makes <c>DELETE repo/{repoId}</c> reachable: it refuses a
/// repo that still has mods, and until now there was no way to empty one.
/// </summary>
public class DeleteModV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/mods/{modId}", Delete)
            .WithTags("Mods");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Delete(
        Guid repoId, string modId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModStorageService storageService,
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

        var versions = await dbContext.ModVersions.GetVersionsOfModAsync(new RepoId(repoId), new ModId(modId), cancellationToken);
        if (versions.Count == 0)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No mod '{modId}' found in repo '{repoId}'"));
        }

        if (await dbContext.Profiles.CheckIfModIsDependedOn(new RepoId(repoId), new ModId(modId), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.ModInUse(new RepoId(repoId), new ModId(modId)));
        }

        // No gap to close: the whole run of sequence numbers goes with the mod.
        dbContext.ModVersions.RemoveRange(versions);

        await unitOfWork.CommitAsync(cancellationToken);

        foreach (var version in versions)
        {
            await storageService.DeleteMod(new RepoId(repoId), new ModId(modId), version.Id, cancellationToken);
        }

        return TypedResults.Ok();
    }
}
