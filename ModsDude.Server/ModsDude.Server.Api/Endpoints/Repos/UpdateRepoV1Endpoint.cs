﻿using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Repositories;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Repos;

public class UpdateRepoV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("repo/{repoId:guid}", UpdateRepo)
            .WithTags("Repos");
    }


    private static async Task<Results<Ok<RepoDto>, BadRequest<CustomProblemDetails>>> UpdateRepo(
        Guid repoId,
        UpdateRepoRequest request,
        ClaimsPrincipal claimsPrincipal,
        IUnitOfWork unitOfWork,
        IRepoRepository repoRepository,
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authResult = await userRepository.GetByIdAsync(claimsPrincipal.GetUserId(), cancellationToken)
            .CheckIsAllowedTo(x => x
                .AccessRepoAtLevel(new RepoId(repoId), RepoMembershipLevel.Admin))
            .MapToBadRequest();
        if (authResult is not null)
        {
            return authResult;
        }

        var repo = await repoRepository.GetById(new RepoId(repoId));
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        var newName = new RepoName(request.Name);
        if (repo.Name != newName)
        {
            if (await repoRepository.CheckNameIsTaken(newName, cancellationToken))
            {
                return TypedResults.BadRequest(Problems.NameTaken(request.Name));
            }
            repo.Name = newName;
        }

        repo.AdapterData = repo.AdapterData with { Configuration = new(request.AdapterConfiguration) };

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(RepoDto.FromModel(repo));
    }


    public record UpdateRepoRequest(string Name, string AdapterConfiguration);
}
