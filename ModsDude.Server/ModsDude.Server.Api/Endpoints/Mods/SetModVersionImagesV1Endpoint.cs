using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
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
            .WithTags("Mods");
    }


    public record SetModVersionImagesRequest(IEnumerable<ModImageReferenceDto> Images);


    private static async Task<Results<Ok<ModDto>, BadRequest<CustomProblemDetails>>> SetImages(
        Guid repoId, string modId, string versionId,
        SetModVersionImagesRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModImageStorageService imageStorageService,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var modVersion = await dbContext.ModVersions.GetAsync(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId), cancellationToken);
        if (modVersion is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No version '{versionId}' of mod '{modId}' found in repo '{repoId}'"));
        }

        var requested = request.Images.ToList();

        if (requested.Select(x => x.Hash).FirstOrDefault(x => !ModImageHash.IsValid(x)) is string invalid)
        {
            return TypedResults.BadRequest(Problems.InvalidImageHash(invalid));
        }

        // Never blocking registration does not mean never checking: a reference to an address
        // nothing was uploaded to renders as a permanently broken image, and the client has just
        // uploaded these, so the check costs nothing it did not already pay.
        var hashes = requested.Select(x => x.Hash).Distinct().ToList();
        var present = await imageStorageService.CheckWhichExist(hashes, cancellationToken);

        if (hashes.Except(present).FirstOrDefault() is string missing)
        {
            return TypedResults.BadRequest(Problems.ImageDoesNotExist(missing));
        }

        var images = requested.Select(ModImageReferenceDto.ToModel).ToList();

        if (!ModVersion.CheckImagesAreValid(images))
        {
            return TypedResults.BadRequest(Problems.InvalidImageSet(new RepoId(repoId), new ModId(modId), new ModVersionId(versionId)));
        }

        modVersion.SetImages(images, timeService.Now());
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(ModDto.FromModel(modVersion));
    }
}
