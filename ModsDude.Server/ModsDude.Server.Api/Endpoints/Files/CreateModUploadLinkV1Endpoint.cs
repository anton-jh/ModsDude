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

namespace ModsDude.Server.Api.Endpoints.Files;

public class CreateModUploadLinkV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("files/createModUploadLink", CreateModUploadLink)
            .WithTags("Files");
    }


    public record CreateModUploadLinkRequest(Guid RepoId, string ModId, string VersionId);

    /// <param name="ContentHashMetadataKey">
    /// The blob metadata entry the client must write the file's SHA-256 into as it uploads. Named
    /// here rather than agreed by convention, because the server is the only party that reads it
    /// back and a silent mismatch would only surface as an unrepairable registration much later.
    /// </param>
    public record CreateModUploadLinkResponse(string Link, string ContentHashMetadataKey);


    public async Task<Results<Ok<CreateModUploadLinkResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CreateModUploadLink(
        CreateModUploadLinkRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModStorageService modStorageService,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(request.RepoId), RepoMembershipLevel.Member))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        // The two refusals below need opposite responses from the client, so they are distinct
        // problems: there is nothing left to do for a registered version, while an unregistered blob
        // is the orphan a failed import left behind and is finished by registering without
        // re-uploading. Answering both with one problem made a failed import unretryable.
        var modVersion = await dbContext.ModVersions.GetAsync(new RepoId(request.RepoId), new ModId(request.ModId), new ModVersionId(request.VersionId), cancellationToken);
        if (modVersion is not null)
        {
            return TypedResults.BadRequest(Problems.ModVersionAlreadyRegistered(new(request.RepoId), new(request.ModId), new(request.VersionId)));
        }

        if (await modStorageService.CheckIfModExists(new(request.RepoId), new(request.ModId), new(request.VersionId), cancellationToken))
        {
            var storedContentHash = await modStorageService.GetRecordedContentHash(new(request.RepoId), new(request.ModId), new(request.VersionId), cancellationToken);

            return TypedResults.BadRequest(Problems.ModFileAlreadyPresent(new(request.RepoId), new(request.ModId), new(request.VersionId), storedContentHash));
        }

        var link = await modStorageService.GetUploadLink(new(request.RepoId), new(request.ModId), new(request.VersionId), cancellationToken);

        return TypedResults.Ok(new CreateModUploadLinkResponse(link, modStorageService.ContentHashMetadataKey));
    }
}
