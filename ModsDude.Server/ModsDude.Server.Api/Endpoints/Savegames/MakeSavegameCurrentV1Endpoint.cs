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
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Points a profile back at one of its past savegames, so that farm follows the mod list again.
/// </summary>
/// <remarks>
/// <para>
/// <b>A swap, not a promotion.</b> A profile has at most one current savegame, so whatever held the
/// slot becomes past in the same breath. There is deliberately no operation that supersedes a
/// savegame on its own: a profile with no current farm is a state reached by deleting the current
/// one, not by pressing something.
/// </para>
/// <para>
/// <b>The order of the two writes is the whole of the difficulty.</b> One current savegame per
/// profile is a filtered unique index, and it refuses the instant where both rows claim the slot -
/// so the incumbent has to be superseded before the incoming row is cleared. A change tracker
/// promises no order between two updates, which is why they are two commits inside one transaction
/// rather than one <c>SaveChanges</c> and a hope. <c>MoveModVersionV1Endpoint</c> takes an ordering
/// through a unique index the same way.
/// </para>
/// <para>
/// <b>What actually changes is which revision the farm runs on.</b> A current savegame follows its
/// profile and is checked out at head; a past one stays pinned to the revision recorded on its head
/// version. Nothing else about either savegame moves - no version is minted, no claim is touched,
/// and a past savegame was playable all along.
/// </para>
/// <para>
/// <b>Archived is a different question and is not asked here.</b> Making an archived savegame
/// current is allowed, and so is superseding one - archiving is the repo-wide visibility state and
/// says nothing about which farm a profile follows. Refusing over it would also make the archived
/// incumbent unreplaceable, which is one of the three ways out this state is meant to have.
/// </para>
/// <para>
/// Member, like publishing and checking in. It supersedes somebody's farm rather than destroying
/// one, and the same click in the other direction puts it back.
/// </para>
/// </remarks>
public class MakeSavegameCurrentV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/savegames/{savegameId:guid}/makeCurrent", MakeCurrent)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok<MakeSavegameCurrentResponse>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> MakeCurrent(
        Guid repoId, Guid savegameId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
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

        // A savegame with no mod list is in no succession, so there is no slot for it to take. The
        // way to give a farm a profile is to republish it; see docs/10-savegame-profile-binding.md.
        if (savegame.ProfileId is not ProfileId profileId)
        {
            return TypedResults.BadRequest(Problems.SavegameHasNoProfile(savegame.Id));
        }

        var now = timeService.Now();

        // Already the current one. Answered rather than refused: whoever asked wanted this profile
        // following this farm, and it is - and a second client that had not caught up yet is exactly
        // who lands here.
        if (savegame.IsCurrent)
        {
            return TypedResults.Ok(new MakeSavegameCurrentResponse(
                await SavegameReads.DescribeAsync(dbContext, savegame, now, cancellationToken),
                null));
        }

        // Found whether or not it is archived, since archiving does not hand the slot back, and null
        // where the profile has been left with no current savegame by a deletion.
        var superseded = await dbContext.Savegames.GetCurrentAsync(new RepoId(repoId), profileId, cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // First, and on its own: the slot has to be empty before anything may take it. The
        // transaction is what makes the halfway state - the profile following nothing - something no
        // other request and no crash can observe.
        superseded?.Supersede(now);
        await unitOfWork.CommitAsync(cancellationToken);

        savegame.MakeCurrent();

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (SavegameConflicts.IsCurrentSavegameConflict(exception))
        {
            // Somebody else took the slot between the read above and this write - a publish, or the
            // same request made for another of the profile's past farms. The index is what decides;
            // the loser is told what changed rather than being left believing it happened.
            return TypedResults.BadRequest(Problems.SavegameCurrentConflict(profileId));
        }

        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(new MakeSavegameCurrentResponse(
            await SavegameReads.DescribeAsync(dbContext, savegame, now, cancellationToken),
            superseded is null ? null : await SavegameReads.DescribeAsync(dbContext, superseded, now, cancellationToken)));
    }


    /// <param name="Savegame">The savegame that is now the profile's current one.</param>
    /// <param name="Superseded">
    /// The farm this one displaced, or <c>null</c> where the profile had none - which is the state a
    /// deleted current savegame leaves behind. Carried so the client can name it in the same
    /// sentence rather than refetching the list to find out what it just moved, the way check-out
    /// carries who it took a claim from.
    /// </param>
    public record MakeSavegameCurrentResponse(SavegameDto Savegame, SavegameDto? Superseded);
}
