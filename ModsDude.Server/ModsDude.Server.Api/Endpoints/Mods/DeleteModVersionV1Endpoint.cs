using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.EntityTypeConfigurations;
using ModsDude.Server.Persistence.Extensions;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Retention;

namespace ModsDude.Server.Api.Endpoints.Mods;

public class DeleteModVersionV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/mods/{modId}/versions/{versionId}", Delete)
            .WithTags("Mods")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>>> Delete(
        Guid repoId, string modId, string versionId,
        ApplicationDbContext dbContext,
        IModStorageService storageService,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        IRetentionUpkeep retentionUpkeep,
        ILogger<DeleteModVersionV1Endpoint> logger,
        CancellationToken cancellationToken)
    {
        if (RequestChecks.ModId(modId, versionId) is { } invalidId)
        {
            return TypedResults.BadRequest(invalidId);
        }

        var siblings = await dbContext.ModVersions.GetVersionsOfModAsync(new RepoId(repoId), new ModId(modId), cancellationToken);

        var modVersion = siblings.FirstOrDefault(x => x.Id == new ModVersionId(versionId));
        // Already gone, which is what was asked for: a repeat of this request gets the same answer.
        if (modVersion is null)
        {
            return TypedResults.Ok();
        }

        // The last version has to go through the mod delete instead, because a mod that exists with
        // no versions is not a state anything else in the system can represent.
        if (siblings.Count == 1)
        {
            return TypedResults.BadRequest(Problems.CannotDeleteOnlyModVersion(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId)));
        }

        if (await dbContext.ProfileRevisions.IsVersionPinnedAsync(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.ModVersionInUse(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId)));
        }

        var remaining = siblings.Where(x => x != modVersion).ToList();

        dbContext.ModVersions.Remove(modVersion);
        ModVersionSequencer.CloseGap(remaining, modVersion, timeService.Now());

        // Off every ignore list in the repo, in the same commit, so a version imported again later is
        // offered again.
        await dbContext.ReleaseModVersionAsync(modVersion.RepoId, modVersion.ModId, modVersion.Id, cancellationToken);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            // Pinned by a revision saved since the check above.
            return TypedResults.BadRequest(Problems.ModVersionInUse(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId)));
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation(ModVersionConstraints.SequenceIndex) || exception is DbUpdateConcurrencyException)
        {
            // A sibling registered, moved or deleted since the read, so the gap this closed is not where
            // it was. Nothing was written; asking again works against the new order.
            return TypedResults.BadRequest(Problems.VersionPlacementConflict(new RepoId(repoId), new ModId(modId)));
        }

        // One version fewer can turn a mod that was outside its window into one winding down.
        await retentionUpkeep.ReleaseModsAsync(modVersion.RepoId, [modVersion.ModId], cancellationToken);

        await ModFileCleanup.DeleteAsync(storageService, logger, modVersion.RepoId, modVersion.ModId, [modVersion.Id], cancellationToken);

        return TypedResults.Ok();
    }
}
