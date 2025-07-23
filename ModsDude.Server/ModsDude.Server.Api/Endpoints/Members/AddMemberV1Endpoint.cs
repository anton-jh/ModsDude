using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Repositories;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Members;

public class AddMemberV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPost("repos/{repoId:guid}/members", AddMember)
            .WithTags("Members");
    }


    private async Task<Results<Ok, BadRequest<CustomProblemDetails>>> AddMember(
        Guid repoId, AddMemberRequest request,
        ClaimsPrincipal claimsPrincipal,
        IUserRepository userRepository,
        IRepoRepository repoRepository,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var authResult = await userRepository.GetByIdAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Member)
                .GrantAccessToRepo(new RepoId(repoId), request.MembershipLevel))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var subjectUser = await userRepository.GetByIdAsync(new UserId(request.UserId), cancellationToken);
        if (subjectUser is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"User with id '{request.UserId}' does not exist"));
        }

        var repo = await repoRepository.GetById(new RepoId(repoId));
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound.With(x => x.Detail = $"Repo with id '{repoId} does not exist'"));
        }

        if (repo.HasMember(subjectUser.Id))
        {
            return TypedResults.BadRequest(Problems.UserAlreadyMember(repo.Id, subjectUser.Id));
        }

        repo.AddMember(subjectUser.Id, request.MembershipLevel);
        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }


    public record AddMemberRequest(string UserId, RepoMembershipLevel MembershipLevel);
}
