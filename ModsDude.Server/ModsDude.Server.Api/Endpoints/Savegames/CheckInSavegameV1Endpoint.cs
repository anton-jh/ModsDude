using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
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
using ModsDude.Server.Persistence.Retention;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Records a new snapshot of a savegame, and hands it back or keeps it checked out.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>BasedOn</c> is the guarantee; the claim is only the manners.</b> Anybody may take a save
/// from anybody, so what stops one person's evening overwriting another's is that a check-in names
/// the snapshot it was built on and is refused when that is no longer the head. Forcing past it is
/// allowed, and the snapshot is stamped <see cref="SavegameSnapshotOrigin.Forced"/> with
/// <c>BaseSnapshot</c> naming what was actually played.
/// </para>
/// <para>
/// <b>A check-in whose bytes equal the head's mints nothing.</b> The head is answered with instead,
/// and the claim is treated exactly as for a check-in that minted one.
/// </para>
/// <para>
/// <b>What happens to the claim</b> is <see cref="SavegameCheckInClaimRule"/>'s: the caller's own
/// claim ends, or stays open when they keep playing. Keeping playing without a claim opens one, and
/// taking one from somebody else needs <see cref="CheckInSavegameRequest.TakeOver"/>.
/// </para>
/// <para>
/// <b>A repeat is answered as the original was.</b> The request id of each person's latest check-in
/// on a savegame is recorded with its answer, and is looked up before anything else, since a repeat
/// would otherwise be refused as stale against the snapshot it minted itself.
/// </para>
/// </remarks>
public class CheckInSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/savegames/{savegameId:guid}/snapshots", CheckIn)
            .WithTags("Savegames")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok<CheckInSavegameResponse>, BadRequest<CustomProblemDetails>>> CheckIn(
        Guid repoId, Guid savegameId,
        CheckInSavegameRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ISavegameStorageService savegameStorageService,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        IRetentionUpkeep retentionUpkeep,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();
        var requestId = new SavegameCheckInRequestId(request.RequestId);

        var savegame = await dbContext.Savegames.GetAsync(new RepoId(repoId), new SavegameId(savegameId), cancellationToken);
        if (savegame is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No savegame '{savegameId}' found in repo '{repoId}'"));
        }

        var previous = await dbContext.SavegameCheckInRequests.FirstOrDefaultAsync(
            x => x.RepoId == savegame.RepoId && x.SavegameId == savegame.Id && x.UserId == userId, cancellationToken);

        if (previous is not null && previous.Answers(requestId))
        {
            return await AnswerAgainAsync(dbContext, previous, cancellationToken);
        }

        var basedOn = new SavegameSnapshotNumber(request.BasedOn);

        // Paired with the savegame's own profile rather than checked on its own: a save that follows
        // no mod list records no revision, and one that follows a mod list has to record which.
        if (savegame.ProfileId is null != request.ProfileRevision is null)
        {
            return TypedResults.BadRequest(Problems.SavegameProfileNotPaired);
        }

        var profileRevision = request.ProfileRevision is int sent ? new RevisionNumber(sent) : (RevisionNumber?)null;

        // Looked up against the profile the savegame follows rather than one the request names, so a
        // check-in cannot quietly move a save onto another profile.
        if (savegame.ProfileId is ProfileId profileId && profileRevision is RevisionNumber played
            && !await dbContext.ProfileRevisions.ExistsAsync(savegame.RepoId, profileId, played, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{profileId.Value}' has no revision {request.ProfileRevision}"));
        }

        // Before storage and the snapshot's constructor see it: both throw where this reports.
        if (!ModImageHash.IsValid(request.ContentHash))
        {
            return TypedResults.BadRequest(Problems.InvalidSavegameContentHash(request.ContentHash));
        }

        // A snapshot whose blob is absent is a head nobody can check out. A refused check-in is
        // retried by uploading and asking again.
        if (!await savegameStorageService.CheckIfSavegameExists(savegame.RepoId, savegame.Id, request.ContentHash, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.SavegameFileDoesNotExist(savegame.RepoId, savegame.Id, request.ContentHash));
        }

        var isStale = basedOn != savegame.HeadSnapshot;

        if (isStale && !request.Force)
        {
            return TypedResults.BadRequest(Problems.SavegameSnapshotStale(savegame.Id, basedOn, savegame.HeadSnapshot));
        }

        var open = await dbContext.SavegameCheckouts.GetOpenCheckoutAsync(savegame.RepoId, savegame.Id, cancellationToken);
        var claim = SavegameCheckInClaimRule.Decide(open, userId, request.KeepPlaying, request.TakeOver);

        if (claim is SavegameCheckInClaim.RefusedHeldByOther)
        {
            return TypedResults.BadRequest(Problems.SavegameClaimHeldByOther(
                savegame.Id, await SavegameReads.ToDtoAsync(dbContext, open!, cancellationToken)));
        }

        var now = timeService.Now();

        var head = await dbContext.SavegameSnapshots.GetRowAsync(
            savegame.RepoId, savegame.Id, savegame.HeadSnapshot, cancellationToken);

        // Nothing happened to the save, so nothing is recorded, and the head is answered with.
        var snapshot = head is not null && head.ContentHash == request.ContentHash
            ? null
            : savegame.CreateSnapshot(
                profileRevision,
                request.ContentHash,
                request.SizeBytes,
                userId,
                now,
                request.Label,
                isStale ? SavegameSnapshotOrigin.Forced : SavegameSnapshotOrigin.CheckedIn,
                basedOn,
                claim.RecordsAgainstOpenClaim() ? open!.Id : null,
                SavegameDetails.From(request.Details));

        var answeredWith = snapshot?.Number ?? savegame.HeadSnapshot;

        var takenFrom = ApplyClaim(dbContext, claim, open, savegame, userId, now, snapshot?.ProfileRevision ?? head?.ProfileRevision);

        if (snapshot is not null)
        {
            dbContext.SavegameSnapshots.Add(snapshot);
        }

        if (previous is null)
        {
            dbContext.SavegameCheckInRequests.Add(new SavegameCheckInRequest(
                savegame.RepoId, savegame.Id, userId, requestId, now, answeredWith, claim.CallerHolds(), takenFrom?.Id));
        }
        else
        {
            previous.Replace(requestId, now, answeredWith, claim.CallerHolds(), takenFrom?.Id);
        }

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same check-in sent twice at once: the other copy committed first, and its answer is
            // this one's too.
            var repeated = await dbContext.SavegameCheckInRequests.AsNoTracking().FirstOrDefaultAsync(
                x => x.RepoId == savegame.RepoId && x.SavegameId == savegame.Id && x.UserId == userId, cancellationToken);

            if (repeated is not null && repeated.Answers(requestId))
            {
                return await AnswerAgainAsync(dbContext, repeated, cancellationToken);
            }

            // Two check-ins holding the same head both computed the same next number, and the primary
            // key let exactly one through. Or two people took the claim at once, and the one-open-claim
            // index did.
            return TypedResults.BadRequest(Problems.SavegameSnapshotStale(savegame.Id, basedOn, savegame.HeadSnapshot));
        }

        if (snapshot is not null)
        {
            await ReleaseAfterNewSnapshotAsync(retentionUpkeep, savegame, cancellationToken);
        }
        else if (claim is SavegameCheckInClaim.OpensCallers or SavegameCheckInClaim.TakesOver
            && savegame.ProfileId is ProfileId heldProfile)
        {
            // The new claim holds revisions, so any of them shown as due to go stops saying so.
            await retentionUpkeep.ReleaseProfileAsync(savegame.RepoId, heldProfile, cancellationToken);
        }

        return TypedResults.Ok(new CheckInSavegameResponse(
            snapshot is not null
                ? await SavegameReads.ToDtoAsync(dbContext, snapshot, cancellationToken)
                : await SavegameReads.ToDtoAsync(dbContext, savegame.RepoId, head!, cancellationToken),
            claim.CallerHolds(),
            takenFrom is null ? null : await SavegameReads.ToDtoAsync(dbContext, takenFrom, cancellationToken)));
    }


    /// <summary>
    /// After a snapshot is minted - by a check-in, a restore or a publish - and safely committed:
    /// clears the deletion schedules it has made wrong. A new snapshot moves the savegame's window,
    /// and the revision it was played on is now held, which can stop the whole profile winding down.
    /// </summary>
    internal static async Task ReleaseAfterNewSnapshotAsync(
        IRetentionUpkeep retentionUpkeep,
        Savegame savegame,
        CancellationToken cancellationToken)
    {
        await retentionUpkeep.ReleaseSavegameAsync(savegame.RepoId, savegame.Id, cancellationToken);

        if (savegame.ProfileId is ProfileId profileId)
        {
            await retentionUpkeep.ReleaseProfileAsync(savegame.RepoId, profileId, cancellationToken);
        }
    }

    /// <returns>The claim taken from somebody else, or null where none was.</returns>
    /// <param name="playsFrom">The revision the save is on once the check-in is done, which is where a new claim's play starts.</param>
    private static SavegameCheckout? ApplyClaim(
        ApplicationDbContext dbContext,
        SavegameCheckInClaim claim,
        SavegameCheckout? open,
        Savegame savegame,
        UserId userId,
        DateTime now,
        RevisionNumber? playsFrom)
    {
        switch (claim)
        {
            case SavegameCheckInClaim.EndsCallers:
                open!.End(now, SavegameCheckoutEndReason.CheckedIn);
                return null;

            case SavegameCheckInClaim.OpensCallers:
                dbContext.SavegameCheckouts.Add(new SavegameCheckout(savegame.RepoId, savegame.Id, userId, now, playsFrom));
                return null;

            case SavegameCheckInClaim.TakesOver:
                open!.End(now, SavegameCheckoutEndReason.TakenOver);
                dbContext.SavegameCheckouts.Add(new SavegameCheckout(savegame.RepoId, savegame.Id, userId, now, playsFrom));
                return open;

            default:
                return null;
        }
    }

    private static async Task<Results<Ok<CheckInSavegameResponse>, BadRequest<CustomProblemDetails>>> AnswerAgainAsync(
        ApplicationDbContext dbContext,
        SavegameCheckInRequest original,
        CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.SavegameSnapshots.GetRowAsync(
            original.RepoId, original.SavegameId, original.AnsweredWith, cancellationToken);

        if (snapshot is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail =
                $"Snapshot {original.AnsweredWith.Value} of savegame '{original.SavegameId.Value}', which this check-in was answered with, has since been deleted."));
        }

        var takenFrom = original.TakenFrom is SavegameCheckoutId id
            ? await dbContext.SavegameCheckouts.AsNoTracking().FirstAsync(x => x.Id == id, cancellationToken)
            : null;

        return TypedResults.Ok(new CheckInSavegameResponse(
            await SavegameReads.ToDtoAsync(dbContext, original.RepoId, snapshot, cancellationToken),
            original.CallerHoldsClaim,
            takenFrom is null ? null : await SavegameReads.ToDtoAsync(dbContext, takenFrom, cancellationToken)));
    }


    /// <param name="RequestId">
    /// Chosen by the client and sent again with a repeat of the same check-in, which is then answered
    /// as the original was.
    /// </param>
    /// <param name="BasedOn">
    /// The snapshot that was checked out and played. A check-in is refused when it is no longer the
    /// head, so that somebody who was away is told rather than silently overwriting an evening.
    /// </param>
    /// <param name="ProfileRevision">
    /// Which revision of the savegame's profile the folder was on when this was played. <c>null</c>,
    /// and only null, for a savegame that follows no mod list.
    /// </param>
    /// <param name="ContentHash">
    /// SHA-256 of the packed save, which is also the address its blob was uploaded to. Equal to the
    /// head's is how "nothing was played" is recognised.
    /// </param>
    /// <param name="Label">What to call this snapshot in the history. Optional.</param>
    /// <param name="Details">What the client's adapter says about the save as it stands now.</param>
    /// <param name="Force">
    /// Check in anyway over a base that is no longer the head. Never a default: it supersedes
    /// somebody's play.
    /// </param>
    /// <param name="KeepPlaying">Carry on holding the save instead of handing it back.</param>
    /// <param name="TakeOver">
    /// With <paramref name="KeepPlaying"/>, take the claim from whoever holds it. Without it, that is
    /// refused as <see cref="Problems.ProblemType.SavegameClaimHeldByOther"/> naming them.
    /// </param>
    public record CheckInSavegameRequest(
        Guid RequestId,
        int BasedOn,
        int? ProfileRevision,
        string ContentHash,
        long SizeBytes,
        string? Label,
        bool Force,
        bool KeepPlaying,
        bool TakeOver,
        IEnumerable<SavegameDetailDto>? Details);

    /// <param name="Snapshot">The snapshot minted, or the head where the save had not changed.</param>
    /// <param name="HoldsClaim">Whether the caller holds the savegame now. The client keeps or hands back its copy by this.</param>
    /// <param name="TakenFrom">The claim taken from somebody else, or null where none was.</param>
    public record CheckInSavegameResponse(
        SavegameSnapshotDto Snapshot,
        bool HoldsClaim,
        SavegameCheckoutDto? TakenFrom);
}
