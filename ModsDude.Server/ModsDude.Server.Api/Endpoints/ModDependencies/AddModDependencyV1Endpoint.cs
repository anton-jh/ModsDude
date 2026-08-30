using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.ModDependencies;

public class AddModDependencyV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/profiles/{profileId:guid}/modDependencies", Add)
            .WithTags("ModDependencies");
    }


    private static async Task<Results<Ok<ModDependencyDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Add(
        Guid repoId, Guid profileId, AddModDependencyRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var profile = await dbContext.Profiles.GetWithModDependenciesAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        var modVersion = await dbContext.ModVersions.GetAsync(new RepoId(repoId), new ModId(request.ModId), new ModVersionId(request.VersionId), cancellationToken);
        if (modVersion is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No version '{request.VersionId}' of mod '{request.ModId}' found in repo '{repoId}'"));
        }

        if (profile.HasDependencyOn(modVersion.ModId))
        {
            return TypedResults.BadRequest(Problems.ModDependencyExists(profile, modVersion.ModId));
        }

        var modDependency = profile.AddDependency(modVersion, request.Locked);
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(ModDependencyDto.FromModel(modDependency));
    }


    public record AddModDependencyRequest(string ModId, string VersionId, bool Locked);
}
