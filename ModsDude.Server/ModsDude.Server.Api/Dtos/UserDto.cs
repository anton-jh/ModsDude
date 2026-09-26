using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// <paramref name="DisplayName"/> is what the user is called and is not unique;
/// <paramref name="Tag"/> is four digits that separate them from anybody else called the same. A
/// client shows the tag only where a list actually holds two of a name - see
/// <see cref="UserTag"/> for why it is not a suffix on the name itself.
/// <paramref name="AvatarHash"/> is the address of their picture in the image store
/// (<c>GET images/{hash}</c>), or null where they are drawn as their initial.
/// </summary>
public record UserDto(string Id, string DisplayName, string Tag, string? AvatarHash)
{
    public static UserDto FromModel(User user)
    {
        return new(user.Id.Value, user.DisplayName.Value, UserTag.For(user.Id), user.AvatarHash);
    }
}
