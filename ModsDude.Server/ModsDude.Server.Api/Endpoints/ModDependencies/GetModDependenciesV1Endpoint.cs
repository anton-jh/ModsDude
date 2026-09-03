using Microsoft.AspNetCore.Http.HttpResults;
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

/// <summary>
/// What a profile pins, at its current revision or at any older one.
/// </summary>
/// <remarks>
/// <para>
/// The only route into a profile's mod list, and it is read-only. Writing is
/// <c>PUT repos/{repoId}/profiles/{profileId}/revisions</c>, which addresses the profile and always
/// means its head - there is deliberately no route that names a revision to write to, which is what
/// makes an old revision read-only without a flag anybody has to check.
/// </para>
/// <para>
/// The response says which revision answered. A client saving afterwards has to name what it was
/// working from, and taking that from the same response it read the list out of is the only form
/// that cannot be stale by the time it is used.
/// </para>
/// </remarks>
public class GetModDependenciesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles/{profileId:guid}/modDependencies", GetAll)
            .WithTags("ModDependencies");
    }


    /// <param name="revision">Which revision to read, or omitted for the profile's current one.</param>
    private static async Task<Results<Ok<GetModDependenciesResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> GetAll(
        Guid repoId, Guid profileId,
        int? revision,
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

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        var requested = revision is int number ? new RevisionNumber(number) : profile.HeadRevision;

        if (requested != profile.HeadRevision
            && !await dbContext.ProfileRevisions.ExistsAsync(profile.RepoId, profile.Id, requested, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{profileId}' has no revision {requested.Value}"));
        }

        // Projected rather than materialized: a revision carries its dependencies as an owned
        // collection and its versions would drag in their owned attribute and image collections for
        // four columns.
        var rows = await dbContext.ProfileRevisions.GetDependencyRowsAsync(
            profile.RepoId, profile.Id, requested, cancellationToken);

        var dtos = rows.Select(x => new ModDependencyDto(x.ModId.Value, x.VersionId.Value, x.ContentHash, x.Locked));

        return TypedResults.Ok(new GetModDependenciesResponse(requested.Value, requested == profile.HeadRevision, dtos));
    }


    /// <param name="Revision">Which revision this list is - what a following save has to be based on.</param>
    /// <param name="IsHead">
    /// Whether it is the profile's current revision. A client reading an older one is looking at
    /// history and has nothing to save.
    /// </param>
    public record GetModDependenciesResponse(int Revision, bool IsHead, IEnumerable<ModDependencyDto> Dependencies);
}
