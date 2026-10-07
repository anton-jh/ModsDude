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

/// <summary>
/// The repo's archived profiles - what its Archive page lists.
/// </summary>
/// <remarks>
/// <b>Guest, like the live list.</b> The archive is not an admin screen: everybody can see what the
/// repo has put away, and only an admin can move anything in or out of it. Hiding the archive from
/// members would make a profile that quietly vanished unexplainable.
/// </remarks>
public class GetArchivedProfilesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles/archived", GetArchivedProfiles)
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Guest);
    }


    private static async Task<Ok<IEnumerable<ProfileDto>>> GetArchivedProfiles(
        Guid repoId,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var profiles = await dbContext.Profiles
            .Where(x => x.RepoId == new RepoId(repoId) && x.ArchivedAt != null)
            // Most recently archived first, and the timestamp is load-bearing rather than decorative:
            // several archived profiles may share a name, and it is the only thing telling them apart.
            .OrderByDescending(x => x.ArchivedAt)
            .ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.RepoId, x.Name, x.HeadRevision, x.ArchivedAt, x.Version })
            .ToListAsync(cancellationToken);

        var dtos = profiles.Select(x => new ProfileDto(
            x.Id.Value, x.RepoId.Value, x.Name.Value, x.HeadRevision.Value, x.ArchivedAt, x.Version));

        return TypedResults.Ok(dtos);
    }
}
