using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Users;

/// <summary>
/// Renames the caller. Only ever the caller: the route has no user in it to be anybody else.
/// </summary>
/// <remarks>
/// Nothing is checked against other users' names, because nothing needs a name to be unique - see
/// <see cref="DisplayName"/>. The identity provider keeps whatever it had; this system stopped
/// reading it after the first sign-in.
/// </remarks>
public class SetDisplayNameV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("users/me/display-name", SetDisplayName)
            .WithTags("Users");
    }


    private static async Task<Results<Ok<CurrentUserDto>, BadRequest<CustomProblemDetails>>> SetDisplayName(
        SetDisplayNameRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        ITimeService timeService,
        CancellationToken cancellationToken)
    {
        if (!DisplayName.TryParse(request.DisplayName, out var displayName, out var error))
        {
            return TypedResults.BadRequest(Problems.InvalidDisplayName(error));
        }

        var user = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            ?? throw new NotAuthenticatedException();

        user.Rename(displayName, timeService.Now());

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(CurrentUserDto.FromModel(user));
    }


    public record SetDisplayNameRequest(string DisplayName);
}
