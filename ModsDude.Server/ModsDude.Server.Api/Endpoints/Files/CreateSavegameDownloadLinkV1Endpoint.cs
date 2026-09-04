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

public class CreateSavegameDownloadLinkV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("files/createSavegameDownloadLink", CreateSavegameDownloadLink)
            .WithTags("Files");
    }


    /// <param name="ContentHash">
    /// Which version's bytes, by the address they are stored at. The version number is not what is
    /// asked for here: the blob is addressed by content, so two versions that were checked in from
    /// the same save read the same blob.
    /// </param>
    public record CreateSavegameDownloadLinkRequest(Guid RepoId, Guid SavegameId, string ContentHash);

    public record CreateSavegameDownloadLinkResponse(string Link);


    public async Task<Results<Ok<CreateSavegameDownloadLinkResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CreateSavegameDownloadLink(
        CreateSavegameDownloadLinkRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ISavegameStorageService savegameStorageService,
        CancellationToken cancellationToken)
    {
        // Guest, unlike the upload counterpart: reading is a Guest operation everywhere else, and a
        // Guest is offered *Take a copy* - looking at a save without taking the claim, which is the
        // whole of what a Guest can do with one.
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(request.RepoId), RepoMembershipLevel.Guest))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        // Before storage sees it — see the upload counterpart for why the check is here as well as
        // there.
        if (!ModImageHash.IsValid(request.ContentHash))
        {
            return TypedResults.BadRequest(Problems.InvalidSavegameContentHash(request.ContentHash));
        }

        if (!await savegameStorageService.CheckIfSavegameExists(new(request.RepoId), new SavegameId(request.SavegameId), request.ContentHash, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.SavegameFileDoesNotExist(new(request.RepoId), new SavegameId(request.SavegameId), request.ContentHash));
        }

        var link = await savegameStorageService.GetDownloadLink(new(request.RepoId), new SavegameId(request.SavegameId), request.ContentHash, cancellationToken);

        return TypedResults.Ok(new CreateSavegameDownloadLinkResponse(link));
    }
}
