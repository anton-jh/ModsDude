using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Dtos;

public record UserDto(string Id, string Username)
{
    public static UserDto FromModel(User user)
    {
        return new(user.Id.Value, user.Username.Value);
    }
}
