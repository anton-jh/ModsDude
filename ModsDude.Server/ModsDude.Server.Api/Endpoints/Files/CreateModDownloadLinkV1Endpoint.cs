using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Files;

public class CreateModDownloadLinkV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("files/createModDownloadLink", CreateModDownloadLink)
            .WithTags("Files");
    }


    public record CreateModDownloadLinkRequest(Guid RepoId, string ModId, string VersionId);
    public record CreateModDownloadLinkResponse(string Link);


    public async Task<Results<Ok<CreateModDownloadLinkResponse>, BadRequest<CustomProblemDetails>>> CreateModDownloadLink(
        CreateModDownloadLinkRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModStorageService modStorageService,
        CancellationToken cancellationToken)
    {
        // Guest, unlike the upload counterpart: reading mods is a Guest operation everywhere else,
        // and a Guest who can see a profile has to be able to apply it.
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(request.RepoId), RepoMembershipLevel.Guest))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        if (!await modStorageService.CheckIfModExists(new(request.RepoId), new(request.ModId), new(request.VersionId), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.ModFileDoesNotExist(new(request.RepoId), new(request.ModId), new(request.VersionId)));
        }

        var link = await modStorageService.GetDownloadLink(new(request.RepoId), new(request.ModId), new(request.VersionId), cancellationToken);

        return TypedResults.Ok(new CreateModDownloadLinkResponse(link));
    }
}
