using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Repos;

public class UpdateRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}", UpdateRepo)
            .WithTags("Repos")
            .RequireRepoLevel(RepoMembershipLevel.Admin);
    }


    private static async Task<Results<Ok<RepoDto>, BadRequest<CustomProblemDetails>>> UpdateRepo(
        Guid repoId,
        UpdateRepoRequest request,
        IUnitOfWork unitOfWork,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var repo = await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken);
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        var name = new RepoName(request.Name);
        var configuration = new AdapterConfiguration(request.AdapterConfiguration);

        // Already so: a repeat of this request - a retry, a second click - is answered as the first was.
        if (repo.Name == name && repo.AdapterData.Configuration == configuration)
        {
            return TypedResults.Ok(RepoDto.FromModel(repo));
        }

        if (repo.Version != request.ExpectedVersion)
        {
            return TypedResults.BadRequest(Problems.RepoChanged);
        }

        repo.Rename(name);
        repo.Configure(configuration);

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.BadRequest(Problems.RepoChanged);
        }

        return TypedResults.Ok(RepoDto.FromModel(repo));
    }


    /// <param name="ExpectedVersion">The version the change was made against. Another one is refused.</param>
    public record UpdateRepoRequest(string Name, string AdapterConfiguration, int ExpectedVersion);
}
