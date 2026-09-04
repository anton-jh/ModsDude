using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Files;

public class CreateSavegameUploadLinkV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("files/createSavegameUploadLink", CreateSavegameUploadLink)
            .WithTags("Files");
    }


    /// <param name="ContentHash">
    /// The SHA-256 of the packed save, which is the address it will be stored at. The client has to
    /// have hashed the file before it asks for a link, and that is the point: naming the bytes up
    /// front is what lets the server check afterwards that what arrived is what was offered.
    /// </param>
    public record CreateSavegameUploadLinkRequest(Guid RepoId, Guid SavegameId, string ContentHash);

    /// <param name="Link">
    /// Where to upload, or <c>null</c> when there is nothing to upload.
    /// </param>
    /// <param name="AlreadyStored">
    /// The bytes are already stored, so skip the upload and check in.
    /// </param>
    /// <param name="ContentHashMetadataKey">
    /// The blob metadata entry the client must write the save's SHA-256 into as it uploads. Named
    /// here rather than agreed by convention, for the reason
    /// <see cref="CreateModUploadLinkV1Endpoint.CreateModUploadLinkResponse"/> gives.
    /// </param>
    public record CreateSavegameUploadLinkResponse(string? Link, bool AlreadyStored, string ContentHashMetadataKey);


    /// <summary>
    /// Mints an upload link for a packed savegame, or reports that the bytes are already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A blob already at the address is a success here, and a refusal in
    /// <see cref="CreateModUploadLinkV1Endpoint"/>.</b> That endpoint addresses a file by the mod
    /// version it belongs to, so a blob sitting at the address holds <em>some other bytes</em>
    /// somebody published under this id - an identity collision that has to be reported before it is
    /// registered over. A savegame blob is addressed by its content, so a blob at the address holds
    /// precisely the bytes being offered, and the only thing left to do with them is nothing.
    /// </para>
    /// <para>
    /// That is what makes re-checking in a 400 MB save free, and it is the ordinary case rather than
    /// an exotic one: a night that changed nothing, and a restore, both offer bytes the repo already
    /// has. So the client is told to skip straight to checking in rather than being handed a link to
    /// re-upload what is already stored.
    /// </para>
    /// </remarks>
    public async Task<Results<Ok<CreateSavegameUploadLinkResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CreateSavegameUploadLink(
        CreateSavegameUploadLinkRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ISavegameStorageService savegameStorageService,
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

        // Before storage sees it. The hash becomes a blob path segment, and the storage layer
        // validates it again on the way to building one - but it throws where this reports, and
        // there is no global exception handler to turn that into anything but a 500.
        if (!ModImageHash.IsValid(request.ContentHash))
        {
            return TypedResults.BadRequest(Problems.InvalidSavegameContentHash(request.ContentHash));
        }

        if (await savegameStorageService.CheckIfSavegameExists(new(request.RepoId), new SavegameId(request.SavegameId), request.ContentHash, cancellationToken))
        {
            return TypedResults.Ok(new CreateSavegameUploadLinkResponse(null, true, savegameStorageService.ContentHashMetadataKey));
        }

        var link = await savegameStorageService.GetUploadLink(new(request.RepoId), new SavegameId(request.SavegameId), request.ContentHash, cancellationToken);

        return TypedResults.Ok(new CreateSavegameUploadLinkResponse(link, false, savegameStorageService.ContentHashMetadataKey));
    }
}
