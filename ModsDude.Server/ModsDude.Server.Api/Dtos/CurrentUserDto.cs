using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Api.Dtos;

/// <summary>
/// The caller's own record, which carries one thing <see cref="UserDto"/> does not:
/// <paramref name="IsTrusted"/>.
/// </summary>
/// <remarks>
/// A separate shape rather than a field on <see cref="UserDto"/>, because that DTO is also how a
/// repo's members are described to each other and whether somebody may create repos is not their
/// teammates' business. Here it exists so a client can say why the option is closed instead of
/// letting the user fill in a form that can only be refused.
/// </remarks>
public record CurrentUserDto(string Id, string DisplayName, string Tag, bool IsTrusted)
{
    public static CurrentUserDto FromModel(User user)
    {
        return new(user.Id.Value, user.DisplayName.Value, UserTag.For(user.Id), user.IsTrusted);
    }
}
