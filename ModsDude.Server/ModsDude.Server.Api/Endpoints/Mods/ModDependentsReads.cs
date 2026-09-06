using Microsoft.AspNetCore.Http.HttpResults;
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
/// Which profiles, and which of their revisions, are keeping a mod in the repo.
/// </summary>
/// <remarks>
/// <para>
/// Read after a delete has been refused rather than before it is offered. The refusal is what the
/// endpoints and the foreign key decide; this turns "a profile depends on it" into a list somebody
/// can act on, which is the whole difference between a dead end and a next step.
/// </para>
/// <para>
/// <b>Two endpoints, one query.</b> Deleting a version and deleting a mod are the two things that
/// get refused, and each wants the answer for exactly what it tried - naming every version's
/// dependents after a single version was refused would list revisions that have nothing to do with
/// it. They are separate classes because <c>MapAllEndpointsFromAssembly</c> names the operation from
/// the class and applies it to the one builder <c>Map</c> returns, so a class mapping two routes
/// leaves one of them unnamed and the generated client calls it something nobody chose.
/// </para>
/// </remarks>
internal static class ModDependentsReads
{
    /// <summary>
    /// Generous enough to be the whole answer in every ordinary case. A repo's dependency rows are
    /// its profile count times its revision count times its profile sizes, so there is a bound; the
    /// response says when it was hit rather than quietly stopping.
    /// </summary>
    private const int _limit = 500;


    /// <param name="versionId">
    /// One version, or null for the whole mod - the two things that can be deleted, and the same
    /// query either way.
    /// </param>
    public static async Task<Results<Ok<ModDependentsDto>, Forbidden<CustomProblemDetails>>> GetAsync(
        Guid repoId, string modId, ModVersionId? versionId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Guest, like every other read of the catalog: it says which profiles pin what, which a
        // Guest can already read one profile at a time.
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Guest))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        // One more than the cap, so "there are more" is answered by the query rather than guessed at
        // from a full page.
        var rows = await dbContext.ProfileRevisions.GetDependentRevisionsAsync(
            new RepoId(repoId), new ModId(modId), versionId, _limit + 1, cancellationToken);

        var truncated = rows.Count > _limit;

        // The names and heads of only the profiles that actually turned up, which is at most as many
        // as the repo has and in practice one or two.
        var profileIds = rows.Select(x => x.ProfileId).Distinct().ToList();

        var profiles = await dbContext.Profiles.GetSummariesAsync(new RepoId(repoId), profileIds, cancellationToken);

        var dependents = rows
            .Take(_limit)
            .GroupBy(x => x.ProfileId)
            .Select(group =>
            {
                var profile = profiles.GetValueOrDefault(group.Key);
                var revisions = group.Select(x => x.Revision).Distinct().OrderBy(x => x.Value).ToList();

                return new ProfileDependentDto(
                    group.Key.Value,
                    // A profile deleted between the refusal and this read is not an error worth
                    // reporting; naming it by id still points at the right thing.
                    profile?.Name.Value ?? group.Key.Value.ToString(),
                    revisions.Select(x => x.Value),
                    profile is not null && revisions.Contains(profile.HeadRevision));
            })
            .OrderBy(x => x.ProfileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return TypedResults.Ok(new ModDependentsDto(dependents, truncated));
    }
}
