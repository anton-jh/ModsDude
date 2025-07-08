using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Persistence.DbContexts;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Users;

public class GetUsersV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("users", GetAll)
            .WithTags("Users");
    }


    private async Task<Ok<IEnumerable<UserDto>>> GetAll(
        ApplicationDbContext dbContext,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        var userId = claimsPrincipal.GetUserId();
        var user = await dbContext.Users.FindAsync([userId], cancellationToken)
            ?? throw new NotAuthenticatedException();

        var repoIds = user.RepoMemberships.Select(x => x.RepoId).ToList();
        var otherMemberships = await dbContext.RepoMemberships
            .Where(x => x.UserId != userId && repoIds.Contains(x.RepoId))
            .ToListAsync(cancellationToken);
        var otherUserIds = otherMemberships.Select(x => x.UserId).ToList();

        var otherUsers = await dbContext.Users
            .AsNoTracking()
            .Where(x => otherUserIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Username
            })
            .ToListAsync(cancellationToken);

        var dtos = otherUsers.Select(x => new UserDto(x.Id.Value, x.Username.Value));

        return TypedResults.Ok(dtos);
    }
}
