using Microsoft.AspNetCore.Http.HttpResults;
using ModsDude.Server.Api.Authorization;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Api.ErrorHandling;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Exceptions;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Security.Claims;

namespace ModsDude.Server.Api.Endpoints.Users;

/// <summary>
/// Points the caller's picture at an image already in the image store. The bytes travel the way
/// every image does - <c>POST images/{hash}</c>, verified against their address on the way in -
/// and this only records which address is theirs.
/// </summary>
/// <remarks>
/// The picture is made on the client, cropped square and sized down before it is hashed, for the
/// same reason mod derivatives are: the server has no image stack. What it can check it does - that
/// the address is one, and that something is stored there - because a reference to nothing would
/// be a picture every teammate's client fails to draw.
/// </remarks>
public class SetAvatarV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapPut("users/me/avatar", SetAvatar)
            .WithTags("Users");
    }


    private static async Task<Results<Ok<CurrentUserDto>, BadRequest<CustomProblemDetails>>> SetAvatar(
        SetAvatarRequest request,
        ClaimsPrincipal claimsPrincipal,
        ApplicationDbContext dbContext,
        IModImageStorageService imageStorageService,
        IUnitOfWork unitOfWork,
        ITimeService timeService,
        CancellationToken cancellationToken)
    {
        if (!ModImageHash.IsValid(request.Hash))
        {
            return TypedResults.BadRequest(Problems.InvalidImageHash(request.Hash));
        }

        var present = await imageStorageService.CheckWhichExist([request.Hash], cancellationToken);
        if (present.Count == 0)
        {
            return TypedResults.BadRequest(Problems.ImageDoesNotExist(request.Hash));
        }

        var user = await dbContext.Users.GetAsync(claimsPrincipal.GetUserId(), cancellationToken)
            ?? throw new NotAuthenticatedException();

        user.SetAvatar(request.Hash, timeService.Now());

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(CurrentUserDto.FromModel(user));
    }


    public record SetAvatarRequest(string Hash);
}
