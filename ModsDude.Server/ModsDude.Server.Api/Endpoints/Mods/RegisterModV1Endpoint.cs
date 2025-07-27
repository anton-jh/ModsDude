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

public class RegisterModV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/mods", RegisterMod)
            .WithTags("Mods");
    }


    public async Task<Results<Ok<ModDto>, BadRequest<CustomProblemDetails>>> RegisterMod(
        Guid repoId,
        RegisterModRequest request,
        ClaimsPrincipal claimsPrincipal,
        IModStorageService storageService,
        ApplicationDbContext dbContext,
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

        if (!await storageService.CheckIfModExists(new RepoId(repoId), new ModId(request.ModId), new ModVersionId(request.VersionId), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.ModFileDoesNotExist(new RepoId(repoId), new ModId(request.ModId), new ModVersionId(request.VersionId)));
        }

        var mod = await dbContext.Mods.GetAsync(new RepoId(repoId), new ModId(request.ModId), cancellationToken);

        if (mod is null)
        {
            mod = new Mod(
                new RepoId(repoId),
                new ModId(request.ModId),
                new ModVersionId(request.VersionId),
                request.Attributes.Select(ModAttributeDto.ToModel),
                timeService.Now(),
                request.Description,
                request.DisplayName);

            dbContext.Mods.Add(mod);
        }
        else
        {
            if (mod.CheckHasVersion(new ModVersionId(request.VersionId)))
            {
                return TypedResults.BadRequest(Problems.ModVersionAlreadyExists(new RepoId(repoId), new ModId(request.ModId), new ModVersionId(request.VersionId)));
            }

            mod.AddVersion(
                new ModVersionId(request.VersionId),
                request.Attributes.Select(ModAttributeDto.ToModel),
                timeService.Now(),
                request.Description,
                request.DisplayName);
        }

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(ModDto.FromModel(mod));
    }


    public record RegisterModRequest(
        string ModId,
        string VersionId,
        string DisplayName,
        string Description,
        IEnumerable<ModAttributeDto> Attributes);
}
