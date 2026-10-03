using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Repos;

/// <summary>
/// Brings an archived repo back, under the name it went away with.
/// </summary>
/// <remarks>
/// The one restore that takes no name, unlike the profile and savegame ones. Those two are unique
/// within their repo, so archiving frees a name somebody else can take and the cost lands on the way
/// back. Repo names are not unique at all, so there is nothing to be taken and nothing to ask.
/// </remarks>
public class RestoreRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/restore", Restore)
            .WithTags("Repos")
            .RequireRepoLevel(RepoMembershipLevel.Admin);
    }


    private static async Task<Results<Ok, BadRequest<CustomProblemDetails>>> Restore(
        Guid repoId,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var repo = await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken);
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        repo.Restore();

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.BadRequest(Problems.RepoChanged);
        }

        return TypedResults.Ok();
    }
}
