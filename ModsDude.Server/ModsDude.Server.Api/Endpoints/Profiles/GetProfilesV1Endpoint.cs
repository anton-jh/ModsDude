using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Profiles;

public class GetProfilesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles", GetAll)
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Guest);
    }


    private static async Task<Results<Ok<IEnumerable<ProfileDto>>, BadRequest<CustomProblemDetails>>> GetAll(
        Guid repoId,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Projected rather than materialized, out of habit rather than necessity now: a profile row
        // no longer carries its mod list, so this is four columns either way.
        // Live profiles only. An archived one still exists and everything pointing at it goes on
        // pointing at it; it is simply not in this list - see the repo's Archive page.
        var profiles = await dbContext.Profiles
            .Where(x => x.RepoId == new RepoId(repoId) && x.ArchivedAt == null)
            .Select(x => new { x.Id, x.RepoId, x.Name, x.HeadRevision, x.ArchivedAt })
            .ToListAsync(cancellationToken);

        var dtos = profiles.Select(x => new ProfileDto(x.Id.Value, x.RepoId.Value, x.Name.Value, x.HeadRevision.Value, x.ArchivedAt));

        return TypedResults.Ok(dtos);
    }
}
