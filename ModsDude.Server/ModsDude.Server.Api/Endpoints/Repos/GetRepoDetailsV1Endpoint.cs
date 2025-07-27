using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Repos;

public class GetRepoDetailsV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repo/{repoId:guid}", GetRepoDetails)
            .WithTags("Repos");
    }


    private async Task<Results<Ok<RepoDetailsDto>, BadRequest<CustomProblemDetails>>> GetRepoDetails(
        [FromRoute] Guid repoId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
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
        
        var repo = await dbContext.Repos.GetAsync(new RepoId(repoId), cancellationToken);
        if (repo is null)
        {
            return TypedResults.BadRequest(Problems.NotFound);
        }

        var memberships = await dbContext.RepoMemberships.GetByRepoIdAsync(new RepoId(repoId), cancellationToken);
        var memberIds = memberships.Select(x => x.UserId).ToList();
        var members = await dbContext.Users.Where(x => memberIds.Contains(x.Id)).ToListAsync(cancellationToken);

        var dto = RepoDetailsDto.FromModel(repo, members.Join(memberships, u => u.Id, m => m.UserId, (u, m) => (u, m)));

        return TypedResults.Ok(dto);
    }
}
