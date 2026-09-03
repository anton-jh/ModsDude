using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
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

public class DeleteModVersionV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/mods/{modId}/versions/{versionId}", Delete)
            .WithTags("Mods");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Delete(
        Guid repoId, string modId, string versionId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModStorageService storageService,
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

        var siblings = await dbContext.ModVersions.GetVersionsOfModAsync(new RepoId(repoId), new ModId(modId), cancellationToken);

        var modVersion = siblings.FirstOrDefault(x => x.Id == new ModVersionId(versionId));
        if (modVersion is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No version '{versionId}' of mod '{modId}' found in repo '{repoId}'"));
        }

        // The last version has to go through the mod delete instead, because a mod that exists with
        // no versions is not a state anything else in the system can represent.
        if (siblings.Count == 1)
        {
            return TypedResults.BadRequest(Problems.CannotDeleteOnlyModVersion(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId)));
        }

        if (await dbContext.ProfileRevisions.CheckIfVersionIsDependedOn(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.ModVersionInUse(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId)));
        }

        var remaining = siblings.Where(x => x != modVersion).ToList();

        dbContext.ModVersions.Remove(modVersion);
        ModVersionSequencer.CloseGap(remaining, modVersion, timeService.Now());

        await unitOfWork.CommitAsync(cancellationToken);

        // After the commit, never before it. A stranded blob is recoverable — the next import of the
        // same version adopts it, and the reclamation sweep collects it otherwise — whereas a
        // registration whose blob is gone can never be repaired, because being registered is exactly
        // what stops an upload link from being minted for it again.
        await storageService.DeleteMod(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId), cancellationToken);

        return TypedResults.Ok();
    }
}
