using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Repositories;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
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
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authResult = await userRepository.GetByIdAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Guest))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var profile = await dbContext.Profiles.FindAsync([new RepoId(repoId), new ProfileId(profileId)], cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        var dto = ProfileDto.FromModel(profile);

        return TypedResults.Ok(dto);
    }
}
