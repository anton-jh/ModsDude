using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.ModDependencies;

public class DeleteModDependencyV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("repos/{repoId:guid}/profiles/{profileId:guid}/modDependencies/{modId}", Delete)
            .WithTags("ModDependencies");
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>>> Delete(
        Guid repoId, Guid profileId, string modId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var profile = await dbContext.Profiles.GetAsync(new RepoId(repoId), new ProfileId(profileId), cancellationToken);
        if (profile is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No profile '{profileId}' found in repo '{repoId}'"));
        }

        if (!profile.HasDependencyOn(new ModId(modId)))
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"No dependency on mod '{modId}' found in profile '{profileId}'"));
        }

        profile.DeleteDependency(new ModId(modId));
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
