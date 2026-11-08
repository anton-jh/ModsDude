using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <summary>
/// Attaches imagery to a version that is already registered. It is a separate call from registration
/// on purpose: the mod file is verified before metadata is written, and images get the opposite
/// treatment. They are decoration, and an import of 2,000 mods must not fail — or worse, half-fail —
/// because a thumbnail upload timed out. Whatever did not make it is picked up later by a client
/// that holds the mod file and notices the gap.
///
/// Unlike the image routes themselves this one is repo-scoped, because it writes into a repo's
/// catalog rather than into the shared address space.
/// </summary>
public class SetModVersionImagesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/mods/{modId}/versions/{versionId}/images", SetImages)
            .WithTags("Mods")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    /// <summary>
    /// Far beyond any real gallery, because each reference costs a call to storage. A safety net
    /// rather than a working limit.
    /// </summary>
    private const int _maximumImages = 100;


    public record SetModVersionImagesRequest(IEnumerable<ModImageReferenceDto> Images);


    private static async Task<Results<Ok<ModDto>, BadRequest<CustomProblemDetails>>> SetImages(
        Guid repoId, string modId, string versionId,
        SetModVersionImagesRequest request,
        ApplicationDbContext dbContext,
        IModImageStorageService imageStorageService,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (RequestChecks.ModId(modId, versionId) is { } invalidId)
        {
            return TypedResults.BadRequest(invalidId);
        }

        var modVersion = await dbContext.ModVersions.GetAsync(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId), cancellationToken);
        if (modVersion is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No version '{versionId}' of mod '{modId}' found in repo '{repoId}'"));
        }

        var requested = request.Images.ToList();

        if (requested.Count > _maximumImages)
        {
            return TypedResults.BadRequest(Problems.BatchTooLarge(requested.Count, _maximumImages));
        }

        if (requested.Select(x => x.Hash).FirstOrDefault(x => !Sha256Hex.IsValid(x)) is string invalid)
        {
            return TypedResults.BadRequest(Problems.InvalidImageHash(invalid));
        }

        var images = requested.Select(ModImageReferenceDto.ToModel).ToList();

        if (!ModVersion.CheckImagesAreValid(images))
        {
            return TypedResults.BadRequest(Problems.InvalidImageSet(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId)));
        }

        // Never blocking registration does not mean never checking: a reference to an address
        // nothing was uploaded to renders as a permanently broken image. Reusing is the check, and
        // also what stops a reclamation sweep that listed an image as unreferenced deleting it from
        // under this call.
        var missing = await imageStorageService.TryReuse([.. requested.Select(x => x.Hash).Distinct()], cancellationToken);

        if (missing.FirstOrDefault() is string absent)
        {
            return TypedResults.BadRequest(Problems.ImageDoesNotExist(absent));
        }

        modVersion.SetImages(images, timeService.Now());
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(ModDto.FromModel(modVersion));
    }
}
