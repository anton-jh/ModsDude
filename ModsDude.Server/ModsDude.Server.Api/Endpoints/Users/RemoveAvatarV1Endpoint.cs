using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Users;

/// <summary>
/// Takes the caller's picture away, leaving their initial on their tag's colour. The image itself is
/// left where it is: another user may point at the same bytes, and the reclamation sweep deletes it
/// once nothing does.
/// </summary>
public class RemoveAvatarV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapDelete("users/me/avatar", RemoveAvatar)
            .WithTags("Users");
    }


    private static async Task<Ok<CurrentUserDto>> RemoveAvatar(
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        ITimeService timeService,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            ?? throw new NotAuthenticatedException();

        user.SetAvatar(null, timeService.Now());

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(CurrentUserDto.FromModel(user));
    }
}
