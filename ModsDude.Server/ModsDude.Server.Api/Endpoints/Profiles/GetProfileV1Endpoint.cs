using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Profiles;

public class GetProfileV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles/{profileId:guid}", GetSingle)
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Guest);
    }


    private static async Task<Results<Ok<ProfileDto>, BadRequest<CustomProblemDetails>>> GetSingle(
        Guid repoId,
        Guid profileId,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles
            .Where(x => x.RepoId == new RepoId(repoId) && x.Id == new ProfileId(profileId))
            // Addressed by id, so an archived profile is still readable through it - a link into one
            // takes you to the profile, by way of the archive rather than instead of it.
            .Select(x => new { x.Id, x.RepoId, x.Name, x.HeadRevision, x.ArchivedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        var dto = new ProfileDto(
            profile.Id.Value, profile.RepoId.Value, profile.Name.Value, profile.HeadRevision.Value, profile.ArchivedAt);

        return TypedResults.Ok(dto);
    }
}
