using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.ModDependencies;

public class GetModDependenciesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles/{profileId:guid}/modDependencies", GetAll)
            .WithTags("ModDependencies");
    }


    private static async Task<Results<Ok<IEnumerable<ModDependencyDto>>, BadRequest<CustomProblemDetails>>> GetAll(
        Guid repoId, Guid profileId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Guest))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        // Projected rather than materialized: ModDependencyDto needs the mod id, which lives behind
        // ModVersion.Mod, and materializing that navigation would drag in every version of every
        // mod the profile depends on via Mod.Versions' auto-include.
        var modDependencies = await dbContext.Profiles
            .Where(x => x.RepoId == new RepoId(repoId) && x.Id == new ProfileId(profileId))
            .SelectMany(x => x.ModDependencies)
            .Select(x => new
            {
                ModId = x.ModVersion.Mod.Id,
                ModVersionId = x.ModVersion.Id,
                x.LockVersion
            })
            .ToListAsync(cancellationToken);

        var dtos = modDependencies
            .Select(x => new ModDependencyDto(x.ModId.Value, x.ModVersionId.Value, x.LockVersion));

        return TypedResults.Ok(dtos);
    }
}
