using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

public class GetProfilesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles", GetAll)
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<IEnumerable<ProfileDto>>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> GetAll(
        Guid repoId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Guest))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        // Projected rather than materialized: ModDependencies is an owned collection, so loading
        // Profile entities would read every profile's entire dependency set only for ProfileDto to
        // discard it — thousands of rows per profile at the volumes this targets.
        var profiles = await dbContext.Profiles
            .Where(x => x.RepoId == new RepoId(repoId))
            .Select(x => new { x.Id, x.RepoId, x.Name })
            .ToListAsync(cancellationToken);

        var dtos = profiles.Select(x => new ProfileDto(x.Id.Value, x.RepoId.Value, x.Name.Value));

        return TypedResults.Ok(dtos);
    }
}
