using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using ModsDude.Server.Persistence.Retention;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// Puts an older revision's mod list back, by copying it to the front as a new revision.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is deleted.</b> Restoring revision 3 while the head is 8 produces revision 9 pinning
/// what 3 pinned. Moving the head backwards instead would strand revisions 4 to 8 as a future
/// nobody can reach, and force a tree the moment anyone saved after rolling back; deleting them
/// would destroy the record of what people were actually running - and can invalidate the sync
/// manifest of an instance that applied one.
/// </para>
/// <para>
/// So a rollback is an ordinary edit whose contents happen to equal an old revision's, and undoing a
/// bad rollback is another rollback. It is recorded as
/// <see cref="ProfileRevisionOrigin.Restored"/> so the history can say where it came from rather
/// than presenting it as a save somebody typed out by hand.
/// </para>
/// <para>
/// Member, like any other save. It discards nothing, and history makes it visible and reversible -
/// which is a better guarantee than a permission level.
/// </para>
/// </remarks>
public class RestoreProfileRevisionV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/profiles/{profileId:guid}/revisions/{number:int}/restore", Restore)
            .WithTags("Profiles")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<ProfileRevisionDto>, BadRequest<CustomProblemDetails>>> Restore(
        Guid repoId, Guid profileId, int number,
        RestoreProfileRevisionRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        IRetentionUpkeep retentionUpkeep,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        var requestId = new ProfileRevisionRequestId(request.RequestId);

        var previousRequest = await ProfileRevisionWrites.FindRequestAsync(dbContext, profile, userId, cancellationToken);
        if (previousRequest is not null && previousRequest.Answers(requestId))
        {
            return await ProfileRevisionWrites.AnswerAgainAsync(dbContext, previousRequest, cancellationToken);
        }

        var basedOn = new RevisionNumber(request.BasedOn);

        if (basedOn != profile.HeadRevision)
        {
            return TypedResults.BadRequest(Problems.ProfileRevisionStale(profile.Id, basedOn, profile.HeadRevision));
        }

        var source = new RevisionNumber(number);

        if (!await dbContext.ProfileRevisions.ExistsAsync(profile.RepoId, profile.Id, source, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{profileId}' has no revision {number}"));
        }

        var pins = await dbContext.ProfileRevisions.GetPinsAsync(profile.RepoId, profile.Id, source, cancellationToken);
        var headPins = await dbContext.ProfileRevisions.GetPinsAsync(profile.RepoId, profile.Id, profile.HeadRevision, cancellationToken);

        var resolved = await ProfileRevisionWrites.ResolveAsync(dbContext, profile.RepoId, pins, cancellationToken);
        if (resolved.Problem is not null)
        {
            // A version an old revision pins cannot have been deleted - the foreign key sees to that
            // - so this is unreachable in practice and reported rather than assumed away.
            return TypedResults.BadRequest(resolved.Problem);
        }

        var now = timeService.Now();

        var revision = profile.CreateRevision(
            resolved.Dependencies!,
            headPins,
            userId,
            now,
            request.Label,
            ProfileRevisionOrigin.Restored,
            sourceRevision: source);

        dbContext.ProfileRevisions.Add(revision);
        await dbContext.ReleasePinnedAsync(profile, pins, cancellationToken);

        ProfileRevisionWrites.RecordRequest(dbContext, previousRequest, profile, userId, requestId, now, revision.Number);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var repeated = await ProfileRevisionWrites.FindRepeatAsync(dbContext, profile, userId, requestId, cancellationToken);
            if (repeated is not null)
            {
                return await ProfileRevisionWrites.AnswerAgainAsync(dbContext, repeated, cancellationToken);
            }

            // Somebody saved against the same head and the primary key let one of them through.
            return TypedResults.BadRequest(Problems.ProfileRevisionStale(profile.Id, basedOn, revision.Number));
        }

        // A new revision moves the profile's window, and pins versions that may have been scheduled.
        await retentionUpkeep.ReleaseProfileAsync(profile.RepoId, profile.Id, cancellationToken);
        await retentionUpkeep.ReleaseModsPinnedByAsync(profile.RepoId, profile.Id, revision.Number, cancellationToken);

        return TypedResults.Ok(await ProfileRevisionWrites.ToDtoAsync(dbContext, revision, cancellationToken));
    }


    /// <param name="RequestId">
    /// Chosen by the client and sent again with a repeat of the same restore, which is then answered
    /// as the original was.
    /// </param>
    /// <param name="BasedOn">
    /// The head the user saw when they chose to restore. Refused when it is no longer the head, so a
    /// restore never silently undoes a save the user has not seen.
    /// </param>
    /// <param name="Label">What to call the restored revision in the history. Optional.</param>
    /// <remarks>
    /// A restore is recorded whether or not it changes anything - unlike a save, which mints nothing
    /// when the list is unchanged. Restoring the revision that is already the head is somebody
    /// asking for it explicitly, and a history that quietly did nothing would read as a bug.
    /// </remarks>
    public record RestoreProfileRevisionRequest(Guid RequestId, int BasedOn, string? Label);
}
