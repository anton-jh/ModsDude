using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
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
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<ProfileRevisionDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Restore(
        Guid repoId, Guid profileId, int number,
        RestoreProfileRevisionRequest? request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();

        var authResult = await dbContext.Users.GetAsync(userId, cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
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

        var source = new RevisionNumber(number);

        if (!await dbContext.ProfileRevisions.ExistsAsync(profile.RepoId, profile.Id, source, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{profileId}' has no revision {number}"));
        }

        var pins = await dbContext.ProfileRevisions.GetPinsAsync(profile.RepoId, profile.Id, source, cancellationToken);
        var previous = await dbContext.ProfileRevisions.GetPinsAsync(profile.RepoId, profile.Id, profile.HeadRevision, cancellationToken);

        var resolved = await ProfileRevisionWrites.ResolveAsync(dbContext, profile.RepoId, pins, cancellationToken);
        if (resolved.Problem is not null)
        {
            // A version an old revision pins cannot have been deleted - the foreign key sees to that
            // - so this is unreachable in practice and reported rather than assumed away.
            return TypedResults.BadRequest(resolved.Problem);
        }

        var revision = profile.CreateRevision(
            resolved.Dependencies!,
            previous,
            userId,
            timeService.Now(),
            request?.Label,
            ProfileRevisionOrigin.Restored,
            sourceRevision: source);

        dbContext.ProfileRevisions.Add(revision);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return TypedResults.BadRequest(Problems.ProfileRevisionStale(profile.Id, profile.HeadRevision, revision.Number));
        }

        return TypedResults.Ok(await ProfileRevisionWrites.ToDtoAsync(dbContext, revision, cancellationToken));
    }


    /// <summary>
    /// A restore is recorded whether or not it changes anything - unlike a save, which mints nothing
    /// when the list is unchanged. Restoring the revision that is already the head is somebody
    /// asking for it explicitly, and a history that quietly did nothing would read as a bug.
    /// </summary>
    public record RestoreProfileRevisionRequest(string? Label);
}
