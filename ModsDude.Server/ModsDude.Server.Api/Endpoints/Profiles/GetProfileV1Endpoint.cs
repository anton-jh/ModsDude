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

namespace ModsDude.Server.Api.Endpoints.Profiles;

public class GetProfileV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profile/{profileId:guid}", GetSingle)
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<ProfileDto>, BadRequest<CustomProblemDetails>>> GetSingle(
        Guid repoId,
        Guid profileId,
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

        // Projected rather than materialized — see GetProfilesV1Endpoint. ModDependencies is owned
        // and would be read in full for a DTO that does not carry it.
        var profile = await dbContext.Profiles
            .Where(x => x.RepoId == new RepoId(repoId) && x.Id == new ProfileId(profileId))
            .Select(x => new { x.Id, x.RepoId, x.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        var dto = new ProfileDto(profile.Id.Value, profile.RepoId.Value, profile.Name.Value);

        return TypedResults.Ok(dto);
    }
}
