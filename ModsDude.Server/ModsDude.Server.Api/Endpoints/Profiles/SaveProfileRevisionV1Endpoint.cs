using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

/// <summary>
/// Saves a profile's whole mod list as a new revision. The only way its contents ever change.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole list, not a diff.</b> A revision is a snapshot, so the request carries what the
/// profile should pin and the server records exactly that. The client already has the whole list in
/// hand - it is the thing on screen - and one request of two thousand pins beats two thousand
/// requests by a margin that needs no arguing.
/// </para>
/// <para>
/// <b>One save is one revision.</b> That is why the per-dependency routes are gone: with them, the
/// server could not see a save at all, so every toggled lock would have been a revision of its own
/// and the history would have been unreadable within a week. The boundary is the Save button the
/// user already presses.
/// </para>
/// <para>
/// <b>A save that changes nothing mints nothing.</b> Opening a profile, looking at it and pressing
/// Save is not an event, and a history that recorded it would bury the events that are.
/// </para>
/// <para>
/// <b><c>BasedOn</c> is what makes concurrent edits safe.</b> Two members editing one profile used
/// to write per-dependency, last write silently winning per mod - a profile could end up as neither
/// person's list. A save now names the revision it was built on and is refused if that is no longer
/// the head, which is a question the old shape could not even ask. See docs/06-flows.md.
/// </para>
/// </remarks>
public class SaveProfileRevisionV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/profiles/{profileId:guid}/revisions", Save)
            .WithTags("Profiles");
    }


    private static async Task<Results<Ok<ProfileRevisionDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Save(
        Guid repoId, Guid profileId,
        SaveProfileRevisionRequest request,
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

        var basedOn = new RevisionNumber(request.BasedOn);

        if (basedOn != profile.HeadRevision)
        {
            return TypedResults.BadRequest(Problems.ProfileRevisionStale(profile.Id, basedOn, profile.HeadRevision));
        }

        var pins = request.Mods
            .Select(x => new ProfileModPin(new ModId(x.ModId), new ModVersionId(x.VersionId), x.Locked))
            .ToList();

        var resolved = await ProfileRevisionWrites.ResolveAsync(dbContext, profile.RepoId, pins, cancellationToken);
        if (resolved.Problem is not null)
        {
            return TypedResults.BadRequest(resolved.Problem);
        }

        var previous = await dbContext.ProfileRevisions.GetPinsAsync(profile.RepoId, profile.Id, profile.HeadRevision, cancellationToken);

        var changes = ProfileRevisionChanges.Between(previous, pins);

        if (changes.IsEmpty)
        {
            // Nothing happened, so nothing is recorded. The head is answered with instead, which is
            // what the client would have been given had it saved.
            var head = await ProfileRevisionReads.GetAsync(dbContext, profile.RepoId, profile.Id, profile.HeadRevision, cancellationToken);

            return head is null
                ? TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{profileId}' has no revision {profile.HeadRevision.Value}"))
                : TypedResults.Ok(head);
        }

        var revision = profile.CreateRevision(
            resolved.Dependencies!,
            previous,
            userId,
            timeService.Now(),
            request.Label,
            ProfileRevisionOrigin.Saved);

        dbContext.ProfileRevisions.Add(revision);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two saves based on the same head both computed the same next number, and the primary
            // key let exactly one of them through. The check above is what usually catches this; the
            // database is what makes it true rather than likely.
            return TypedResults.BadRequest(Problems.ProfileRevisionStale(profile.Id, basedOn, revision.Number));
        }

        return TypedResults.Ok(await ProfileRevisionWrites.ToDtoAsync(dbContext, revision, cancellationToken));
    }


    /// <param name="BasedOn">
    /// The revision this list was built from. A save is refused when it is no longer the head, so
    /// that a member editing a stale copy is told rather than silently overwriting somebody.
    /// </param>
    /// <param name="Label">What to call this save in the history. Optional; most saves are not named.</param>
    /// <param name="Mods">
    /// Everything the profile should pin, in full. Anything absent is removed, which is what makes
    /// this a snapshot rather than a patch.
    /// </param>
    public record SaveProfileRevisionRequest(int BasedOn, string? Label, IEnumerable<ProfileModPinRequest> Mods);

    /// <param name="Locked">
    /// The profile's own lock. The adapter's - <see cref="ModVersion.Locked"/> - is a fact about the
    /// mod and is never sent, because a client that thought it had changed would be writing a claim
    /// it has no standing to make.
    /// </param>
    public record ProfileModPinRequest(string ModId, string VersionId, bool Locked);
}
