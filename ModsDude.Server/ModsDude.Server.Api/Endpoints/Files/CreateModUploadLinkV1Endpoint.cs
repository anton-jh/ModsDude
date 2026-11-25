using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Api.Maintenance;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Storage;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Files;

public class CreateModUploadLinkV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("files/createModUploadLink", CreateModUploadLink)
            .WithTags("Files");
    }


    /// <param name="UploadId">
    /// Null for a new upload. To renew a link that expired partway, the upload it was issued for: the
    /// renewed link names the same blob, so the blocks already sent stay usable.
    /// </param>
    public record CreateModUploadLinkRequest(Guid RepoId, string ModId, string VersionId, long SizeBytes, Guid? UploadId);

    /// <param name="ContentHashMetadataKey">
    /// The blob metadata entry the client must write the file's SHA-256 into as it uploads. Named
    /// here rather than agreed by convention, because the server is the only party that reads it
    /// back and a silent mismatch would only surface as an unrepairable registration much later.
    /// </param>
    /// <param name="Link">Where to upload: the upload's own blob, never the version's address.</param>
    /// <param name="UploadId">The upload to name when registering, and when renewing the link.</param>
    public record CreateModUploadLinkResponse(string Link, Guid UploadId, string ContentHashMetadataKey);


    public async Task<Results<Ok<CreateModUploadLinkResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CreateModUploadLink(
        CreateModUploadLinkRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModStorageService modStorageService,
        IStorageMaintenance storageMaintenance,
        IOptions<StorageMaintenanceOptions> maintenanceOptions,
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

        if (RequestChecks.ModId(request.ModId, request.VersionId) is { } invalidId)
        {
            return TypedResults.BadRequest(invalidId);
        }

        if (request.SizeBytes < 0)
        {
            return TypedResults.BadRequest(Problems.InvalidFileSize(request.SizeBytes));
        }

        // The refusals below need different responses from the client, so they are distinct problems:
        // there is nothing left to do for a registered version; a file copied into place by a
        // registration that never committed is finished by registering without re-uploading; and the file
        // of a deleted version is deleted here so the version can be uploaded again, or makes the client
        // try again later.
        var modVersion = await dbContext.ModVersions.GetAsync(new RepoId(request.RepoId), new ModId(request.ModId), new ModVersionId(request.VersionId), cancellationToken);
        if (modVersion is not null)
        {
            return TypedResults.BadRequest(Problems.ModVersionAlreadyRegistered(new(request.RepoId), new(request.ModId), new(request.VersionId)));
        }

        if (await modStorageService.GetStoredMod(new(request.RepoId), new(request.ModId), new(request.VersionId), cancellationToken) is { } stored)
        {
            var deletion = maintenanceOptions.Value.Enabled
                ? await storageMaintenance.DeleteReleasedModFileAsync(
                    new RepoId(request.RepoId), new ModId(request.ModId), new ModVersionId(request.VersionId),
                    maintenanceOptions.Value.MinimumSoftDeleteDays,
                    cancellationToken)
                : ReleasedModFileDeletion.Refused;

            switch (deletion)
            {
                case ReleasedModFileDeletion.Deleted:
                    break;
                case ReleasedModFileDeletion.NotReleased:
                    return TypedResults.BadRequest(Problems.ModFileAlreadyPresent(new(request.RepoId), new(request.ModId), new(request.VersionId), stored.RecordedContentHash));
                case ReleasedModFileDeletion.Registered:
                    return TypedResults.BadRequest(Problems.ModVersionAlreadyRegistered(new(request.RepoId), new(request.ModId), new(request.VersionId)));
                case ReleasedModFileDeletion.Refused:
                    return TypedResults.BadRequest(Problems.ModFileAwaitingDeletion(new(request.RepoId), new(request.ModId), new(request.VersionId)));
            }
        }

        var uploadId = new UploadId(request.UploadId ?? Guid.NewGuid());

        // Queued before the link exists, so an upload that is never registered is collected, and again
        // when the link is renewed, so an upload under way stays queued as long as it needs.
        await dbContext.QueueUploadAsync(
            StorageContainer.Mods,
            BlobNames.Upload(new RepoId(request.RepoId), userId, uploadId),
            new RepoId(request.RepoId),
            userId,
            cancellationToken);

        var link = await modStorageService.GetUploadLink(new(request.RepoId), userId, uploadId, cancellationToken);

        dbContext.FileTransfers.Add(new FileTransfer(new(request.RepoId), userId, TransferredFile.Mod, TransferDirection.Upload, request.SizeBytes, timeService.Now()));
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(new CreateModUploadLinkResponse(link, uploadId.Value, modStorageService.ContentHashMetadataKey));
    }
}
