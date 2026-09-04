using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Profiles;

public class DeleteProfileV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/profiles/{profileId:guid}", Delete)
            .WithTags("Profiles");
    }

    
    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> Delete(
        Guid repoId, Guid profileId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
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

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        // The database enforces this too - both foreign keys onto a profile from the savegame
        // aggregate are Restrict - so this check exists to make the refusal readable rather than to
        // make it true. Reported the way ModVersionInUse is, and racing past it between the check
        // and the commit still lands on the foreign key.
        if (await dbContext.Savegames.CheckIfUsedBySavegameAsync(dbContext.SavegameVersions, new RepoId(repoId), new ProfileId(profileId), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.ProfileInUseBySavegame(new RepoId(repoId), new ProfileId(profileId)));
        }

        dbContext.Profiles.Remove(profile);
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
