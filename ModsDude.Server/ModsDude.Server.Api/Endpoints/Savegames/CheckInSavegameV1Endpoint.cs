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
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Hands a savegame back, as a new snapshot of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Check-in asks nothing about which savegame it is.</b> The route names one and the client knows
/// it from the checkout binding it wrote when the save went into the slot. Choosing between twenty
/// near-identical folders from memory is where the MVP went wrong, and it is precisely the moment
/// where a wrong answer publishes somebody else's slot under this save's name and burns a snapshot
/// doing it.
/// </para>
/// <para>
/// <b><c>BasedOn</c> is the guarantee; the claim is only the manners.</b> Anybody may take a save
/// from anybody, so what actually stops one person's evening overwriting another's is that a
/// check-in names the snapshot it was built on and is refused when that is no longer the head. The
/// claim is what makes that refusal rare - the base check is what makes it impossible to lose play
/// silently.
/// </para>
/// <para>
/// <b>Forcing is allowed, and leaves the fork in the record.</b> Somebody who has played four hours
/// on a base that has since moved is not helped by being told no and nothing else. The forced
/// check-in becomes the head, stamped <see cref="SavegameSnapshotOrigin.Forced"/> with
/// <c>BaseSnapshot</c> naming what was actually played, so the history says a fork happened and
/// which snapshot was superseded - without anybody having to render a tree.
/// </para>
/// <para>
/// <b>A check-in whose bytes equal the head's mints nothing.</b> Launching the game, looking at it
/// and quitting must not cost a 400 MB blob and a line of history. The head is answered with
/// instead, exactly as a profile save that changes nothing answers with its head.
/// </para>
/// <para>
/// <b>The claim is only ended when the caller is the one holding it.</b> A forced check-in is
/// routinely made by somebody who never had the save - that is what forcing is - and ending
/// somebody else's claim as <see cref="SavegameCheckoutEndReason.CheckedIn"/> would put a sentence
/// in the log that never happened. Their claim stands, and the new head is what tells them they were
/// overtaken.
/// </para>
/// </remarks>
public class CheckInSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/savegames/{savegameId:guid}/snapshots", CheckIn)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok<SavegameSnapshotDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CheckIn(
        Guid repoId, Guid savegameId,
        CheckInSavegameRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ISavegameStorageService savegameStorageService,
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

        var savegame = await dbContext.Savegames.GetAsync(new RepoId(repoId), new SavegameId(savegameId), cancellationToken);
        if (savegame is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No savegame '{savegameId}' found in repo '{repoId}'"));
        }

        var basedOn = new SavegameSnapshotNumber(request.BasedOn);

        // Paired with the savegame's own profile rather than checked on its own: a save that follows
        // no mod list records no revision, and one that follows a mod list has to record which. The
        // request cannot decide either way - the savegame does - so a mismatch is refused rather
        // than being resolved in one direction or the other.
        if (savegame.ProfileId is null != request.ProfileRevision is null)
        {
            return TypedResults.BadRequest(Problems.SavegameProfileNotPaired);
        }

        var profileRevision = request.ProfileRevision is int sent ? new RevisionNumber(sent) : (RevisionNumber?)null;

        // The revision is looked up against the profile the savegame follows rather than one the
        // request names, so a check-in cannot quietly move a save onto another profile. Nothing
        // moves a save between profiles at all; see UpdateSavegameV1Endpoint.
        if (savegame.ProfileId is ProfileId profileId && profileRevision is RevisionNumber played
            && !await dbContext.ProfileRevisions.ExistsAsync(savegame.RepoId, profileId, played, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{profileId.Value}' has no revision {request.ProfileRevision}"));
        }

        // Before storage sees it, and before the snapshot's own constructor does. Both validate the
        // hash again, and both throw where this reports - and there is no global handler to turn a
        // domain validation exception into anything but a 500.
        if (!ModImageHash.IsValid(request.ContentHash))
        {
            return TypedResults.BadRequest(Problems.InvalidSavegameContentHash(request.ContentHash));
        }

        // Before anything is written, and for the same reason as at publish: a snapshot whose blob is
        // absent is a head nobody can check out, and the savegame is stuck there until somebody
        // restores past it. A refused check-in is retried by uploading and asking again.
        if (!await savegameStorageService.CheckIfSavegameExists(savegame.RepoId, savegame.Id, request.ContentHash, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.SavegameFileDoesNotExist(savegame.RepoId, savegame.Id, request.ContentHash));
        }

        var isStale = basedOn != savegame.HeadSnapshot;

        if (isStale && !request.Force)
        {
            // The head is carried in the problem so the client can say what it is now rather than
            // only that it is not what was sent - and so the person can decide to force past it,
            // which is a decision only they can make.
            return TypedResults.BadRequest(Problems.SavegameSnapshotStale(savegame.Id, basedOn, savegame.HeadSnapshot));
        }

        var now = timeService.Now();

        var head = await dbContext.SavegameSnapshots.GetRowAsync(
            savegame.RepoId, savegame.Id, savegame.HeadSnapshot, cancellationToken);

        if (head is not null && head.ContentHash == request.ContentHash)
        {
            // Nothing happened to the save, so nothing is recorded. The head is answered with
            // instead, which is what the client would have been given had a snapshot been minted.
            //
            // The claim still ends: the person pressed check in, and leaving them holding a save
            // they have just handed back would keep the slot claimed for a night that never
            // happened.
            await EndOwnCheckoutAsync(dbContext, savegame, userId, now, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return TypedResults.Ok(await SavegameReads.ToDtoAsync(dbContext, savegame.RepoId, head, cancellationToken));
        }

        var checkout = await EndOwnCheckoutAsync(dbContext, savegame, userId, now, cancellationToken);

        var snapshot = savegame.CreateSnapshot(
            profileRevision,
            request.ContentHash,
            request.SizeBytes,
            userId,
            now,
            request.Label,
            isStale ? SavegameSnapshotOrigin.Forced : SavegameSnapshotOrigin.CheckedIn,
            basedOn,
            // Null where somebody else holds the save, which is the ordinary shape of a forced
            // check-in: the snapshot was not checked in against any claim of this person's.
            checkout?.Id,
            SavegameDetails.From(request.Details));

        dbContext.SavegameSnapshots.Add(snapshot);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two check-ins holding the same head both computed the same next number, and the
            // primary key let exactly one of them through. The check above is what gives the good
            // error message; the key is what makes it a guarantee rather than a likelihood.
            return TypedResults.BadRequest(Problems.SavegameSnapshotStale(savegame.Id, basedOn, snapshot.Number));
        }

        // After the check-in is safely committed, never in the same transaction as it: a prune that
        // fails must not take somebody's play down with it.
        await SavegamePruning.PruneAsync(dbContext, savegame, cancellationToken);

        return TypedResults.Ok(await SavegameReads.ToDtoAsync(dbContext, snapshot, cancellationToken));
    }


    /// <summary>
    /// Ends the caller's own claim on the savegame as checked in, and returns it - or <c>null</c>
    /// where the caller was not the one holding it, which is not an error.
    /// </summary>
    private static async Task<SavegameCheckout?> EndOwnCheckoutAsync(
        ApplicationDbContext dbContext,
        Savegame savegame,
        UserId userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var checkout = await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(savegame.RepoId, savegame.Id, cancellationToken);

        if (checkout is null || checkout.UserId != userId)
        {
            return null;
        }

        checkout.End(now, SavegameCheckoutEndReason.CheckedIn);

        return checkout;
    }


    /// <param name="BasedOn">
    /// The snapshot that was checked out and played. A check-in is refused when it is no longer the
    /// head, so that somebody who was away is told rather than silently overwriting an evening.
    /// </param>
    /// <param name="ProfileRevision">
    /// Which revision of the savegame's profile the folder was actually on when this was played.
    /// Recorded rather than derived, because the truth about a save is the mod list it ran against
    /// and not the one the profile happens to be at now.
    /// <para>
    /// <c>null</c>, and only null, for a savegame that follows no mod list: there is no revision to
    /// name and nothing observed one. Sending it for such a save, or omitting it for one that does
    /// follow a list, is refused rather than guessed at.
    /// </para>
    /// </param>
    /// <param name="ContentHash">
    /// SHA-256 of the packed save, which is also the address its blob was uploaded to. Equal to the
    /// head's is how "nothing was played" is recognised, and it costs nothing to send.
    /// </param>
    /// <param name="Label">What to call this snapshot in the history. Optional; most check-ins are not named.</param>
    /// <param name="Details">
    /// What the client's adapter says about the save as it stands now. Recorded per snapshot rather
    /// than per savegame, because a map and a playtime describe the bytes being checked in.
    /// </param>
    /// <param name="Force">
    /// Check in anyway over a base that is no longer the head. Never a default: it supersedes
    /// somebody's play, so it is a decision a person makes after being told what they are about to
    /// do.
    /// </param>
    public record CheckInSavegameRequest(
        int BasedOn,
        int? ProfileRevision,
        string ContentHash,
        long SizeBytes,
        string? Label,
        bool Force,
        IEnumerable<SavegameDetailDto>? Details);
}
