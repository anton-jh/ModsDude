using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Endpoints.Repos;

/// <summary>
/// Creates a repo with the caller as its first Admin. A repeat of the same request returns the repo
/// it made.
/// </summary>
public class CreateRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos", CreateRepo)
            .WithTags("Repos");
    }


    private static async Task<Results<Ok<RepoDto>, BadRequest<CustomProblemDetails>, Forbidden<CustomProblemDetails>>> CreateRepo(
        CreateRepoRequest request,
        IUnitOfWork unitOfWork,
        ITimeService timeService,
        ApplicationDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (RequestChecks.Name(request.Name) is { } invalid)
        {
            return TypedResults.BadRequest(invalid);
        }

        var userId = httpContext.User.GetUserId();
        var user = await dbContext.Users.GetAsync(userId, cancellationToken);

        var authResult = user
            .CheckIsAllowedTo(x => x
                .CreateRepo())
            .MapToForbidden();
        if (authResult is not null)
        {
            return authResult;
        }

        var requestId = new RepoCreateRequestId(request.RequestId);

        if (await FindRepeatAsync(dbContext, userId, requestId, cancellationToken) is Repo original)
        {
            return TypedResults.Ok(RepoDto.FromModel(original));
        }

        var now = timeService.Now();

        // Nothing to check the name against. Somebody else's repo may already be called this, and so
        // may one of the caller's own - RepoTag is what keeps the two apart in a list.
        var repo = new Repo(new RepoName(request.Name), now, user)
        {
            AdapterData = new AdapterData(
                new AdapterIdentifier(request.AdapterId),
                new AdapterConfiguration(request.AdapterConfiguration))
        };
        dbContext.Repos.Add(repo);
        dbContext.RepoCreateRequests.Add(new RepoCreateRequest(userId, requestId, repo.Id, now));

        try
        {
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            dbContext.ChangeTracker.Clear();

            // The same request sent twice at once: the other copy committed first, and its repo is
            // this one's answer too.
            var concurrent = await FindRepeatAsync(dbContext, userId, requestId, cancellationToken);
            if (concurrent is null)
            {
                throw;
            }

            return TypedResults.Ok(RepoDto.FromModel(concurrent));
        }

        return TypedResults.Ok(RepoDto.FromModel(repo));
    }

    private static Task<Repo?> FindRepeatAsync(
        ApplicationDbContext dbContext,
        UserId userId,
        RepoCreateRequestId requestId,
        CancellationToken cancellationToken)
        => dbContext.RepoCreateRequests
            .Where(x => x.UserId == userId && x.RequestId == requestId)
            .Join(dbContext.Repos, x => x.RepoId, x => x.Id, (_, repo) => repo)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);


    /// <param name="RequestId">
    /// Chosen by the client and sent again with a repeat of the same request, which then returns the
    /// repo it made.
    /// </param>
    public record CreateRepoRequest(Guid RequestId, string Name, string AdapterId, string AdapterConfiguration);
}
