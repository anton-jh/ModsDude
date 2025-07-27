using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Members;

public class UpdateMembershipV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repos/{repoId:guid}/members/{userId}", UpdateMembership)
            .WithTags("Members");
    }


    private async Task<Results<Ok, BadRequest<CustomProblemDetails>>> UpdateMembership(
        Guid repoId, string userId,
        UpdateMembershipRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var repo = await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken);
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Repo '{repoId}' does not exist"));
        }

        var subjectMembership = repo.GetMembership(new UserId(userId));
        if (subjectMembership is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Member '{userId}' not found"));
        }

        var authResult = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .ChangeOthersMembership(subjectMembership)
                .GrantAccessToRepo(new RepoId(repoId), request.NewLevel))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        subjectMembership.Level = request.NewLevel;
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }


    public record UpdateMembershipRequest(RepoMembershipLevel NewLevel);
}
