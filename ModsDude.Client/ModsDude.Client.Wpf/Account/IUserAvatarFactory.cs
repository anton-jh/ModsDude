using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// Builds the avatar for a person, starting the load of their picture where they have one.
/// </summary>
public interface IUserAvatarFactory
{
    AvatarViewModel Create(string tag, string displayName, string? avatarHash);
}


public static class UserAvatarFactoryExtensions
{
    public static AvatarViewModel Create(this IUserAvatarFactory factory, UserDto user)
        => factory.Create(user.Tag, user.DisplayName, user.AvatarHash);

    public static AvatarViewModel Create(this IUserAvatarFactory factory, CurrentUserDto user)
        => factory.Create(user.Tag, user.DisplayName, user.AvatarHash);
}
