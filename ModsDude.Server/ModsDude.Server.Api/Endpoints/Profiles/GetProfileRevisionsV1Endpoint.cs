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

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// A profile's history, newest first.
/// </summary>
/// <remarks>
/// Readable at Guest. Somebody who syncs a profile without curating it is exactly the person who
/// wants to know what changed under them and when - and, when a save breaks their game, which
/// revision to ask an editor to restore.
/// </remarks>
public class GetProfileRevisionsV1Endpoint : IEndpoint
{
    private const int _defaultLimit = 50;
    private const int _maximumLimit = 200;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/profiles/{profileId:guid}/revisions", Get)
            .WithTags("Profiles");
    }


    /// <param name="skip">How many of the newest revisions to pass over. See the response for why this is an offset.</param>
    private static async Task<Results<Ok<GetProfileRevisionsResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Get(
        Guid repoId, Guid profileId,
        int? skip, int? limit,
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

        var pageSize = Math.Clamp(limit ?? _defaultLimit, 1, _maximumLimit);
        var offset = Math.Max(skip ?? 0, 0);

        var revisions = await ProfileRevisionReads.GetHistoryAsync(
            dbContext, profile.RepoId, profile.Id, offset, pageSize, cancellationToken);

        return TypedResults.Ok(new GetProfileRevisionsResponse(
            revisions,
            profile.HeadRevision.Value,
            offset + revisions.Count < profile.HeadRevision.Value));
    }


    /// <param name="HeadRevision">
    /// Which of them is current. Carried so that a history page does not have to infer "this is the
    /// live one" from the first row of a listing it may have paged into.
    /// </param>
    /// <param name="HasMore">
    /// Whether older revisions remain. The window is an offset from the newest, because
    /// <see cref="RevisionNumber"/> is a value object and a provider cannot translate a comparison
    /// on one - so a page read while somebody is saving can repeat a row.
    /// </param>
    public record GetProfileRevisionsResponse(IEnumerable<ProfileRevisionDto> Revisions, int HeadRevision, bool HasMore);
}
