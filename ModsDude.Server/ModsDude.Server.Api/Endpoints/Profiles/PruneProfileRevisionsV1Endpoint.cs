using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// Deletes old revisions of a profile, which is how the mod versions they pin stop being
/// undeletable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Admin only.</b> Keeping history is what makes an old revision reproducible, and throwing it
/// away is not part of running a repo - it is the deliberate reclaiming of space, which is exactly
/// the shape of thing that belongs to whoever is responsible for the repo rather than to whoever
/// happens to be editing a profile.
/// </para>
/// <para>
/// <b>Numbers are not renumbered.</b> Pruning leaves the gap where a revision was, the same way
/// savegame version numbers already do: a number exists to be said out loud, and renumbering would
/// make yesterday's sentence point at a different mod list.
/// </para>
/// <para>
/// <b>The head is refused, always.</b> It is what the profile currently pins, what a sync applies
/// and what the next save is built on. Emptying a profile is done by editing it, not by deleting
/// what it says.
/// </para>
/// <para>
/// <b>Deletes what it can and reports what it cannot.</b> A batch that refused wholesale because one
/// revision was played on a savegame would make pruning a hundred revisions an exercise in
/// bisection. What comes back names the savegame versions holding each refused revision, so the
/// next step is a link rather than a guess.
/// </para>
/// </remarks>
public class PruneProfileRevisionsV1Endpoint : IEndpoint
{
    /// <summary>
    /// One page of a history is fifty rows, and selecting every row of several pages is a plausible
    /// thing to do. Beyond this the transaction is doing enough row deletion to be worth splitting.
    /// </summary>
    private const int _maximumBatch = 500;


    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        // POST rather than DELETE: the request carries a body naming what to remove, and a DELETE
        // with a body is the kind of thing proxies drop.
        return builder.MapPost("repos/{repoId:guid}/profiles/{profileId:guid}/revisions/prune", Prune)
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<PruneProfileRevisionsResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Prune(
        Guid repoId, Guid profileId,
        PruneProfileRevisionsRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Admin))
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var requested = request.Revisions.Distinct().Select(x => new RevisionNumber(x)).ToList();

        if (requested.Count == 0)
        {
            return TypedResults.Ok(new PruneProfileRevisionsResponse(0, []));
        }

        if (requested.Count > _maximumBatch)
        {
            return TypedResults.BadRequest(Problems.BatchTooLarge(requested.Count, _maximumBatch));
        }

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{profileId}' does not exist in repo '{repoId}'"));
        }

        var existing = await dbContext.ProfileRevisions.GetExistingAsync(
            new RepoId(repoId), profile.Id, requested, cancellationToken);

        var blocked = new List<BlockedRevisionDto>();
        var deletable = new List<RevisionNumber>();

        // Asked once for the whole batch rather than per revision: one query answers "which of these
        // was played on", and a hundred revisions is a hundred round trips otherwise.
        var played = await dbContext.SavegameVersions.GetDependentSavegameVersionsAsync(
            new RepoId(repoId), profile.Id, requested, cancellationToken);

        var savegameNames = await dbContext.Savegames.GetNamesAsync(
            new RepoId(repoId), [.. played.Select(x => x.SavegameId).Distinct()], cancellationToken);

        var playedByRevision = played.ToLookup(x => x.Revision);

        foreach (var revision in requested)
        {
            // Silently fine: something else already deleted it, which is the state the caller wanted.
            if (existing.Contains(revision) is false)
            {
                continue;
            }

            if (revision == profile.HeadRevision)
            {
                blocked.Add(new BlockedRevisionDto(revision.Value, BlockedRevisionReason.IsHead, []));
                continue;
            }

            if (playedByRevision[revision].Any())
            {
                blocked.Add(new BlockedRevisionDto(
                    revision.Value,
                    BlockedRevisionReason.PlayedOn,
                    [.. playedByRevision[revision].Select(x => new SavegameVersionRefDto(
                        x.SavegameId.Value,
                        savegameNames.TryGetValue(x.SavegameId, out var name) ? name.Value : x.SavegameId.Value.ToString(),
                        x.Number.Value))]));

                continue;
            }

            deletable.Add(revision);
        }

        var deleted = await dbContext.ProfileRevisions.DeleteRevisionsAsync(
            new RepoId(repoId), profile.Id, deletable, cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(new PruneProfileRevisionsResponse(deleted, blocked));
    }
}
