using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Savegames;

/// <summary>
/// Brings an archived savegame back into the repo's list, optionally under a new name.
/// </summary>
public class RestoreSavegameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/savegames/{savegameId:guid}/restore", Restore)
            .WithTags("Savegames")
            .RequireRepoLevel(RepoMembershipLevel.Member);
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>>> Restore(
        Guid repoId, Guid savegameId,
        RestoreRequest? request,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (RequestChecks.OptionalName(request?.Name) is { } invalid)
        {
            return TypedResults.BadRequest(invalid);
        }

        var savegame = await dbContext.Savegames.GetAsync(new RepoId(repoId), new SavegameId(savegameId), cancellationToken);
        if (savegame is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        SavegameName? name = request?.Name is { Length: > 0 } requested
            ? new SavegameName(requested)
            : null;

        // Already restored: a repeat of this request is answered as the first was. A rename of a live
        // savegame is UpdateSavegame's, which checks the version it was made against.
        if (savegame.IsArchived is false)
        {
            return name is null || name == savegame.Name
                ? TypedResults.Ok()
                : TypedResults.BadRequest(Problems.NotArchived("Savegame", savegameId).With(x => x.Detail = $"Savegame '{savegameId}' is not archived, so it cannot be restored."));
        }

        var wanted = name ?? savegame.Name;

        // Where the clash deferred by archiving is finally resolved. The filtered unique index says
        // the same thing underneath; this is what turns it into a message.
        if (await dbContext.Savegames.CheckNameIsTaken(new RepoId(repoId), savegame.Id, wanted, cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(wanted.Value));
        }

        savegame.Restore(name);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.BadRequest(Problems.SavegameChanged);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return TypedResults.BadRequest(Problems.NameTaken(wanted.Value));
        }

        return TypedResults.Ok();
    }
}
