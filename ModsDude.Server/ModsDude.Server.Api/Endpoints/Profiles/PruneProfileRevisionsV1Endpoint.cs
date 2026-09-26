using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Retention;
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
/// savegame snapshot numbers already do: a number exists to be said out loud, and renumbering would
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
/// bisection. What comes back names the savegame snapshots holding each refused revision, so the
/// next step is a link rather than a guess.
/// </para>
/// <para>
/// <b>A checked-out save holds revisions too.</b> Its play has not been checked in, so no snapshot
/// names the revision it is on yet - but the check-in will, and is refused if that revision has gone.
/// Each open claim holds its revision and every later one; see <c>SavegameCheckout.HoldsFromRevision</c>.
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
        RetentionUpkeep retentionUpkeep,
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
        var played = await dbContext.SavegameSnapshots.GetDependentSavegameSnapshotsAsync(
            new RepoId(repoId), profile.Id, requested, cancellationToken);

        // Claims are few - one per savegame at most - so every one on the profile is read and matched
        // per revision here rather than asked about revision by revision.
        var checkouts = await dbContext.SavegameCheckouts.GetCheckoutRevisionHoldsAsync(
            dbContext.Savegames, new RepoId(repoId), profile.Id, cancellationToken);

        var savegameNames = await dbContext.Savegames.GetNamesAsync(
            new RepoId(repoId),
            [.. played.Select(x => x.SavegameId).Concat(checkouts.Select(x => x.SavegameId)).Distinct()],
            cancellationToken);

        var holderNames = await dbContext.Users.GetDisplayNamesAsync(
            [.. checkouts.Select(x => x.HeldBy).Distinct()], cancellationToken);

        string NameOf(SavegameId savegameId)
            => savegameNames.TryGetValue(savegameId, out var name) ? name.Value : savegameId.Value.ToString();

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
                blocked.Add(new BlockedRevisionDto(revision.Value, BlockedRevisionReason.IsHead, [], []));
                continue;
            }

            var holding = checkouts.Where(x => x.Holds(revision)).ToList();

            if (holding.Count > 0 || playedByRevision[revision].Any())
            {
                blocked.Add(new BlockedRevisionDto(
                    revision.Value,
                    holding.Count > 0 ? BlockedRevisionReason.CheckedOut : BlockedRevisionReason.PlayedOn,
                    [.. playedByRevision[revision].Select(x => new SavegameSnapshotRefDto(
                        x.SavegameId.Value,
                        NameOf(x.SavegameId),
                        x.Number.Value))],
                    [.. holding.Select(x => new CheckedOutSavegameRefDto(
                        x.SavegameId.Value,
                        NameOf(x.SavegameId),
                        ProfileRevisionReads.Describe(x.HeldBy, holderNames.TryGetValue(x.HeldBy, out var holder) ? holder : null),
                        x.TakenAt))]));

                continue;
            }

            deletable.Add(revision);
        }

        var deleted = await dbContext.ProfileRevisions.DeleteRevisionsAsync(
            new RepoId(repoId), profile.Id, deletable, cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        // Fewer revisions can turn a history that was outside its window into one winding down.
        await retentionUpkeep.ReleaseProfileAsync(profile.RepoId, profile.Id, cancellationToken);

        return TypedResults.Ok(new PruneProfileRevisionsResponse(deleted, blocked));
    }
}
