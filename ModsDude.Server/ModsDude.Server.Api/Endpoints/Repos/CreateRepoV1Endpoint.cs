using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Repos;

public class CreateRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/create", CreateRepo)
            .RequireAuthorization()
            .WithTags("Repos");
    }


    private static async Task<Results<Ok<RepoDto>, BadRequest<CustomProblemDetails>>> CreateRepo(
        CreateRepoRequest request,
        IUnitOfWork unitOfWork,
        ITimeService timeService,
        ApplicationDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        var isTrusted = (await dbContext.Users
            .FirstAsync(x => x.Id == userId, cancellationToken))
            .IsTrusted;
        if (!isTrusted)
        {
            return TypedResults.BadRequest(Problems.NotAuthorized);
        }

        if (await dbContext.Repos.CheckNameIsTaken(new RepoName(request.Name), cancellationToken))
        {
            return TypedResults.BadRequest(Problems.NameTaken(request.Name));
        }

        var repo = new Repo(new RepoName(request.Name), timeService.Now(), userId)
        {
            AdapterData = new AdapterData(
                new AdapterIdentifier(request.AdapterId),
                new AdapterConfiguration(request.AdapterConfiguration))
        };
        dbContext.Repos.Add(repo);
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(RepoDto.FromModel(repo));
    }


    public record CreateRepoRequest(string Name, string AdapterId, string AdapterConfiguration);
}
