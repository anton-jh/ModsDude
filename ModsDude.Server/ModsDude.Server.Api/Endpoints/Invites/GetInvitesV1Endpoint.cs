using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Authorization;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Invites;

/// <summary>
/// Every invite ever made for the repo, spent ones included: the count of who came in through which
/// code is the only record of it there is, and it would be lost by hiding the code once it stopped
/// working.
/// </summary>
public class GetInvitesV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("repos/{repoId:guid}/invites", GetInvites)
            .WithTags("Invites");
    }


    private async Task<Results<Ok<IEnumerable<RepoInviteDto>>, Forbidden<CustomProblemDetails>>> GetInvites(
        Guid repoId,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        ITimeService timeService,
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

        var now = timeService.Now();
        var invites = await dbContext.RepoInvites.GetForRepoAsync(new RepoId(repoId), cancellationToken);

        return TypedResults.Ok(invites.Select(x => RepoInviteDto.FromModel(x, now)));
    }
}
