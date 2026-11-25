using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
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
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Files;

public class CreateSavegameDownloadLinkV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("files/createSavegameDownloadLink", CreateSavegameDownloadLink)
            .WithTags("Files");
    }


    /// <param name="ContentHash">
    /// Which snapshot's bytes, by the address they are stored at. The snapshot number is not what is
    /// asked for here: the blob is addressed by content, so two snapshots that were checked in from
    /// the same save read the same blob.
    /// </param>
    public record CreateSavegameDownloadLinkRequest(Guid RepoId, Guid SavegameId, string ContentHash);

    public record CreateSavegameDownloadLinkResponse(string Link);


    public async Task<Results<Ok<CreateSavegameDownloadLinkResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CreateSavegameDownloadLink(
        CreateSavegameDownloadLinkRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ISavegameStorageService savegameStorageService,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();

        // Guest, unlike the upload counterpart: reading is a Guest operation everywhere else, and a
        // Guest is offered *Take a copy* - looking at a save without taking the claim, which is the
        // whole of what a Guest can do with one.
        var authResult = await dbContext.Users.GetAsync(userId, cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(request.RepoId), RepoMembershipLevel.Guest))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        // Before storage sees it — see the upload counterpart for why the check is here as well as
        // there.
        if (!Sha256Hex.IsValid(request.ContentHash))
        {
            return TypedResults.BadRequest(Problems.InvalidSavegameContentHash(request.ContentHash));
        }

        // Only bytes a snapshot holds: those of pruned snapshots, or never checked in, stay unreadable
        // while they wait to be collected.
        var repoId = new RepoId(request.RepoId);
        var savegameId = new SavegameId(request.SavegameId);

        var sizeBytes = await dbContext.SavegameSnapshots
            .Where(x => x.RepoId == repoId && x.SavegameId == savegameId && x.ContentHash == request.ContentHash)
            .Select(x => (long?)x.SizeBytes)
            .MaxAsync(cancellationToken);

        if (sizeBytes is not long size)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No snapshot of savegame '{request.SavegameId}' in repo '{request.RepoId}' holds '{request.ContentHash}'"));
        }

        var link = await savegameStorageService.GetDownloadLink(repoId, savegameId, request.ContentHash, cancellationToken);

        dbContext.FileTransfers.Add(new FileTransfer(repoId, userId, TransferredFile.Savegame, TransferDirection.Download, size, timeService.Now()));
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(new CreateSavegameDownloadLinkResponse(link));
    }
}
