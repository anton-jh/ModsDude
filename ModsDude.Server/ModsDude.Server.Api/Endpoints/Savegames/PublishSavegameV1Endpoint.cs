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
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Puts a save that only existed in somebody's game folder into the repo, as a new savegame with a
/// first version, held by whoever published it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Publish is not check-in.</b> "Upload this new thing" and "upload a new version of that thing"
/// have opposite failure modes - the first can collide with a name, the second with somebody else's
/// play - and the old MVP made them one button. They are two routes here so that neither can
/// silently do the other's job.
/// </para>
/// <para>
/// <b>Publishing leaves the save checked out to the publisher.</b> Somebody who has just uploaded
/// the farm they are playing has not handed it back, and a publish that left the slot free would
/// invite the next person to take a save whose owner is still in it. The claim is opened in the same
/// transaction as the savegame and its first version, so there is no window in which the save exists
/// unheld.
/// </para>
/// <para>
/// <b>The bytes are checked for before anything is written.</b> A registration whose blob is absent
/// is worse than a failed publish: the savegame exists, its head names an address nothing is stored
/// at, and the only way out is deleting it - whereas a refused publish is retried by uploading and
/// asking again.
/// </para>
/// <para>
/// <b>The savegame's id is the client's to choose</b>, because the blob address contains it. The
/// bytes are uploaded to <c>{repoId}/{savegameId}/{contentHash}</c> before this route is called, so
/// a server-minted id would address a blob nobody had written to. It is a fresh GUID either way; the
/// only difference is which end says it first.
/// </para>
/// <para>
/// <b>Publishing to a profile supersedes whatever farm it was following</b>, in the same transaction
/// as everything else here. A profile has at most one current savegame, so the new one taking the
/// slot and the old one leaving it are one event and not two - and a publish that committed the new
/// row first would be refused by the one-current-savegame index rather than doing half the job. The
/// old savegame is untouched otherwise: still playable, still holding its history, still on the
/// revision it was last played on.
/// </para>
/// <para>
/// <b>A profile is a choice, not a requirement.</b> Publishing without one produces a savegame that
/// follows no mod list: nothing is superseded, no revision is recorded, and every rule about
/// revisions leaves it alone. That is the only shape available to an adapter with savegame support
/// and no mod support, and it is offered in a mod-capable repo too.
/// </para>
/// </remarks>
public class PublishSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/savegames", Publish)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok<SavegameDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Publish(
        Guid repoId,
        PublishSavegameRequest request,
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

        var savegameId = new SavegameId(request.SavegameId);

        // Both or neither. Half a pair is refused here rather than at the check constraint behind
        // it, because "a revision of which profile?" is a question the request cannot answer and a
        // database error is not the way to say so.
        if (request.ProfileId is null != request.ProfileRevision is null)
        {
            return TypedResults.BadRequest(Problems.SavegameProfileNotPaired);
        }

        var profileId = request.ProfileId is Guid chosen ? new ProfileId(chosen) : (ProfileId?)null;
        var profileRevision = request.ProfileRevision is int declared ? new RevisionNumber(declared) : (RevisionNumber?)null;

        if (await dbContext.Savegames.CheckNameIsTaken(new RepoId(repoId), new SavegameName(request.Name), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(request.Name));
        }

        // The revision has to exist before the version can name it: the foreign key onto it is
        // Restrict, so a revision that is not there surfaces as a database error rather than as the
        // answer "that mod list is not one of this profile's".
        // Both patterns, though the check above already made them one condition: the pair being set
        // together is a fact about the request, and stating it here is what lets the compiler agree
        // rather than being told to.
        if (profileId is ProfileId profile && profileRevision is RevisionNumber declaredRevision
            && !await dbContext.ProfileRevisions.ExistsAsync(new RepoId(repoId), profile, declaredRevision, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Profile '{request.ProfileId}' has no revision {request.ProfileRevision}"));
        }

        // Before storage sees it, and before the version's own constructor does. Both validate the
        // hash again, and both throw where this reports - and there is no global handler to turn a
        // domain validation exception into anything but a 500.
        if (!ModImageHash.IsValid(request.ContentHash))
        {
            return TypedResults.BadRequest(Problems.InvalidSavegameContentHash(request.ContentHash));
        }

        if (!await savegameStorageService.CheckIfSavegameExists(new RepoId(repoId), savegameId, request.ContentHash, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.SavegameFileDoesNotExist(new RepoId(repoId), savegameId, request.ContentHash));
        }

        var now = timeService.Now();

        // At most one - the index says so - and found whether or not it is archived, since archiving
        // does not hand a profile's slot back.
        var superseded = profileId is ProfileId target
            ? await dbContext.Savegames.GetCurrentAsync(new RepoId(repoId), target, cancellationToken)
            : null;

        var savegame = new Savegame(new RepoId(repoId), new SavegameName(request.Name), profileId, now)
        {
            Id = savegameId
        };

        // Origin.Created rather than CheckedIn: this version was not built on anything, which is
        // also why it is the one version whose BaseVersion is null.
        var version = savegame.CreateVersion(
            profileRevision,
            request.ContentHash,
            request.SizeBytes,
            userId,
            now,
            request.Label,
            SavegameVersionOrigin.Created,
            details: SavegameDetails.From(request.Details));

        // The version carries no CheckoutId even though a claim is opened beside it. CheckoutId
        // names the claim a version was checked in *against*, and this claim starts here rather
        // than ending here - the play it will eventually record has not happened yet.
        var checkout = new SavegameCheckout(new RepoId(repoId), savegameId, userId, now);

        // Two writes rather than one, and the order is the point: the outgoing savegame has to leave
        // the profile's slot before the new one takes it, or the one-current-savegame index refuses
        // the instant where both are current. A change tracker promises no order between an update
        // and an insert, so this states it - the same shape MoveModVersionV1Endpoint uses to take an
        // ordering through a unique index. The transaction is what makes the halfway state, where
        // the profile has no current savegame at all, something no other request and no crash can
        // observe.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        superseded?.Supersede(now);
        await unitOfWork.CommitAsync(cancellationToken);

        dbContext.Savegames.Add(savegame);
        dbContext.SavegameVersions.Add(version);
        dbContext.SavegameCheckouts.Add(checkout);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Somebody else published to this profile in the same instant, superseded the same
            // incumbent, and got their row in first. The read above cannot see it - it committed
            // after this request read - so the index is what decides, and the loser is told what has
            // changed rather than being told about a name they did not clash with.
            if (profileId is ProfileId contested && SavegameConflicts.IsCurrentSavegameConflict(exception))
            {
                return TypedResults.BadRequest(Problems.SavegameCurrentConflict(contested));
            }

            // Two people published the same name in the same instant. The check above is what gives
            // the good error message; the unique index on (RepoId, Name) is what makes one of them
            // lose rather than both succeeding. A client re-sending a publish it already made lands
            // here too, on the primary key rather than the name - the same refusal, and the right
            // one, since the savegame it is asking for exists.
            return TypedResults.BadRequest(Problems.NameTaken(request.Name));
        }

        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(await SavegameReads.DescribeAsync(dbContext, savegame, now, cancellationToken));
    }


    /// <param name="SavegameId">
    /// The id the client uploaded the bytes under, and the id the savegame is created with. See the
    /// remarks on the endpoint for why this end chooses it.
    /// </param>
    /// <param name="ProfileId">
    /// The profile this save follows from now on, and the one its first version was played on -
    /// which is the same profile for the whole of its life, since nothing moves a save between them.
    /// <para>
    /// <c>null</c> is <b>no mod list</b>, offered in the publish dialog as an explicit choice rather
    /// than being what an omitted field falls back to. The save is then unmanaged: nothing about
    /// revisions, current or past applies to it.
    /// </para>
    /// </param>
    /// <param name="ProfileRevision">
    /// Which revision of that profile the save was played against, or <c>null</c> with a null
    /// <paramref name="ProfileId"/>. Half a pair is refused.
    /// <para>
    /// <b>Declared rather than observed</b>, and the only version in the system of which that is
    /// true: the bytes predate ModsDude, so nothing knows which mods were in the folder while that
    /// farm was actually played, and requiring the profile to be applied first would only observe a
    /// different moment. Never derived from the profile's head here either - the client sends the
    /// number it showed the person, so the declaration is one they saw.
    /// </para>
    /// </param>
    /// <param name="ContentHash">
    /// SHA-256 of the packed save, which is also the address its blob was uploaded to.
    /// </param>
    /// <param name="Label">What to call this first version in the history. Optional.</param>
    /// <param name="Details">
    /// What the client's adapter says about the save - the map, when it was played. Opaque here and
    /// never parsed; optional, because an adapter that describes nothing is a perfectly ordinary one.
    /// </param>
    public record PublishSavegameRequest(
        Guid SavegameId,
        string Name,
        Guid? ProfileId,
        int? ProfileRevision,
        string ContentHash,
        long SizeBytes,
        string? Label,
        IEnumerable<SavegameDetailDto>? Details);
}
