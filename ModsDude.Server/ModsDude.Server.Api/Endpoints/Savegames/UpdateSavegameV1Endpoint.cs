using Microsoft.AspNetCore.Http.HttpResults;
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
/// Renames a savegame, or moves it onto a different profile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Moving a savegame changes intent, not history.</b> <see cref="Savegame.ProfileId"/> is the
/// standing statement that this save follows that profile; every version keeps naming the revision
/// it was actually played on. Branch a profile, move the save onto the branch, and the old versions
/// still honestly say which mod list produced them - rewriting them to agree with the new profile
/// would be inventing play that never happened.
/// </para>
/// <para>
/// So this endpoint touches no version, and the client's next check-in is the first one that records
/// a revision of the new profile.
/// </para>
/// </remarks>
public class UpdateSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/savegames/{savegameId:guid}", Update)
            .WithTags("Savegames");
    }


    private static async Task<Results<Ok<SavegameDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Update(
        Guid repoId, Guid savegameId,
        UpdateSavegameRequest request,
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

        // The overload that excludes this savegame, so that saving the row unchanged - which is what
        // moving it to another profile does to the name - is not refused as a clash with itself.
        if (await dbContext.Savegames.CheckNameIsTaken(new RepoId(repoId), savegame.Id, new SavegameName(request.Name), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(request.Name));
        }

        var profileId = new ProfileId(request.ProfileId);

        // Checked rather than left to the foreign key, which is Restrict and would surface as a
        // database error rather than as the answer "that profile is not in this repo".
        if (await dbContext.Profiles.GetAsync(new RepoId(repoId), profileId, cancellationToken) is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{request.ProfileId}' found in repo '{repoId}'"));
        }

        savegame.Name = new SavegameName(request.Name);
        savegame.ProfileId = profileId;

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(await SavegameReads.DescribeAsync(dbContext, savegame, timeService.Now(), cancellationToken));
    }


    /// <param name="ProfileId">
    /// The profile the save follows from now on. Sent whole rather than as an optional change,
    /// because a rename and a move are the same edit on the same form and a client that could omit
    /// one would have to be trusted to know which.
    /// </param>
    public record UpdateSavegameRequest(string Name, Guid ProfileId);
}
