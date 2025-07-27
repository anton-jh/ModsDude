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
    public record CreateModUploadLinkResponse(string Link);


    public async Task<Results<Ok<CreateModUploadLinkResponse>, BadRequest<CustomProblemDetails>>> CreateModUploadLink(
        CreateModUploadLinkRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModStorageService modStorageService,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(request.RepoId), RepoMembershipLevel.Member))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var mod = await dbContext.Mods.GetAsync(new RepoId(request.RepoId), new ModId(request.ModId), cancellationToken);
        if (mod is not null && mod.CheckHasVersion(new ModVersionId(request.VersionId)))
        {
            return TypedResults.BadRequest(Problems.ModVersionAlreadyExists(new(request.RepoId), new(request.ModId), new(request.VersionId)));
        }

        if (await modStorageService.CheckIfModExists(new(request.RepoId), new(request.ModId), new(request.VersionId), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.ModVersionAlreadyExists(new(request.RepoId), new(request.ModId), new(request.VersionId)));
        }

        var link = await modStorageService.GetUploadLink(new(request.RepoId), new(request.ModId), new(request.VersionId), cancellationToken);

        return TypedResults.Ok(new CreateModUploadLinkResponse(link));
    }
}
