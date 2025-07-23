using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ModsDude.Server.Api.Dtos;
using ModsDude.Server.Application.Repositories;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Endpoints.Users;

public class SearchUserV1Endpoint : IEndpoint
{
    public RouteHandlerBuilder Map(IEndpointRouteBuilder builder)
    {
        return builder.MapGet("users/search", Search)
            .WithTags("Users");
    }


    private async Task<Ok<SearchUserResponse>> Search(
        [FromQuery] string username,
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByUsernameAsync(new Username(username), cancellationToken);

        var dto = user is null
            ? null
            : new UserDto(user.Id.Value, user.Username.Value);

        return TypedResults.Ok(new SearchUserResponse(dto));
    }


    public record SearchUserResponse(UserDto? User);
}
