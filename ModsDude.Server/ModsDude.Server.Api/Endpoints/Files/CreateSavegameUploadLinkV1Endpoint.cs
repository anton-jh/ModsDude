using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Storage;
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
    /// The SHA-256 of the save's contents, which is the address it will be stored at. Checking out
    /// unpacks the archive and checks its contents against it; the server never sees them.
    /// </param>
    /// <param name="SizeBytes">How long the archive is. Checking in refuses an upload of another length.</param>
    /// <param name="UploadId">
    /// Null for a new upload. To renew a link that expired partway, the upload it was issued for: the
    /// renewed link names the same blob, so the blocks already sent stay usable.
    /// </param>
    public record CreateSavegameUploadLinkRequest(Guid RepoId, Guid SavegameId, string ContentHash, long SizeBytes, Guid? UploadId);

    /// <param name="Link">
    /// Where to upload, or <c>null</c> when there is nothing to upload. The upload's own blob, never the
    /// save's address.
    /// </param>
    /// <param name="UploadId">The upload to name when checking in, and when renewing the link. Null with <paramref name="AlreadyStored"/>.</param>
    /// <param name="AlreadyStored">
    /// The bytes are already stored, so skip the upload and check in.
    /// </param>
    /// <param name="ContentHashMetadataKey">
    /// The blob metadata entry the client must write the save's SHA-256 into as it uploads. Named
    /// here rather than agreed by convention, for the reason
    /// <see cref="CreateModUploadLinkV1Endpoint.CreateModUploadLinkResponse"/> gives.
    /// </param>
    public record CreateSavegameUploadLinkResponse(string? Link, Guid? UploadId, bool AlreadyStored, string ContentHashMetadataKey);


    /// <summary>
    /// Mints an upload link for a packed savegame, or reports that the bytes are already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A blob already at the address is a success here, and a refusal in
    /// <see cref="CreateModUploadLinkV1Endpoint"/>.</b> That endpoint addresses a file by the mod
    /// version it belongs to, so a blob sitting at the address may hold <em>some other bytes</em>
    /// somebody published under this id - an identity collision that has to be reported before it is
    /// registered over. A savegame blob is addressed by its contents, so a blob at the address holds the
    /// save being offered, and the only thing left to do with it is nothing. The server can't look
    /// inside an archive, so this rests on the uploader that stored it; checking out verifies it.
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
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();

        var authResult = await dbContext.Users.GetAsync(userId, cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(request.RepoId), RepoMembershipLevel.Member))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        if (request.SizeBytes < 0)
        {
            return TypedResults.BadRequest(Problems.InvalidFileSize(request.SizeBytes));
        }

        // Before storage sees it. The hash becomes a blob path segment, and the storage layer
        // validates it again on the way to building one - but it throws, which reaches the client as
        // a generic InvalidRequest rather than this.
        if (!Sha256Hex.IsValid(request.ContentHash))
        {
            return TypedResults.BadRequest(Problems.InvalidSavegameContentHash(request.ContentHash));
        }

        if (await savegameStorageService.ClaimSavegame(new(request.RepoId), new SavegameId(request.SavegameId), request.ContentHash, null, cancellationToken))
        {
            return TypedResults.Ok(new CreateSavegameUploadLinkResponse(null, null, true, savegameStorageService.ContentHashMetadataKey));
        }

        var uploadId = new UploadId(request.UploadId ?? Guid.NewGuid());

        // Queued before the link exists, so an upload that is never checked in is collected, and again
        // when the link is renewed, so an upload under way stays queued as long as it needs.
        await dbContext.QueueUploadAsync(
            StorageContainer.Savegames,
            BlobNames.Upload(new RepoId(request.RepoId), userId, uploadId),
            new RepoId(request.RepoId),
            userId,
            cancellationToken);

        var link = await savegameStorageService.GetUploadLink(new(request.RepoId), userId, uploadId, cancellationToken);

        dbContext.FileTransfers.Add(new FileTransfer(new(request.RepoId), userId, TransferredFile.Savegame, TransferDirection.Upload, request.SizeBytes, timeService.Now()));
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(new CreateSavegameUploadLinkResponse(link, uploadId.Value, false, savegameStorageService.ContentHashMetadataKey));
    }
}
