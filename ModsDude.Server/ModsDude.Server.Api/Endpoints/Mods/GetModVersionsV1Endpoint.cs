using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Mods;

/// <summary>
/// One mod's versions, oldest first. Unpaged deliberately: this is bounded by how many releases a
/// single mod has had, not by the size of the repo.
/// </summary>
/// <remarks>
/// It exists for the client that has just been told its placement no longer matches the ordering.
/// Recovering from that means re-reading one mod's order, and the only other way to do it is to page
/// through every version in the repo looking for the one mod, which is the wrong shape by three
/// orders of magnitude.
/// </remarks>
public class GetModVersionsV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/mods/{modId}/versions", GetVersions)
            .WithTags("Mods");
    }


    private static async Task<Results<Ok<GetModVersionsResponse>, BadRequest<CustomProblemDetails>>> GetVersions(
        Guid repoId, string modId,
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

        var versions = await dbContext.ModVersions
            .Where(x => x.RepoId == new RepoId(repoId) && x.ModId == new ModId(modId))
            .OrderBy(x => x.SequenceNumber)
            .ToListAsync(cancellationToken);

        // An empty list rather than a not-found: a mod is only the set of versions carrying its id,
        // so there is no mod record that could be missing.
        return TypedResults.Ok(new GetModVersionsResponse(versions.Select(ModDto.FromModel)));
    }


    public record GetModVersionsResponse(IEnumerable<ModDto> Versions);
}
