using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

public interface IUserAccountService
{
    Task<CurrentUserDto> SetDisplayName(string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="picture"/> at its own address, unless something already is, and makes
    /// it this user's.
    /// </summary>
    /// <param name="picture">
    /// Already cropped, sized and encoded the way it is to be drawn - the server stores what it is
    /// given and has no image stack to make it so.
    /// </param>
    Task<CurrentUserDto> SetAvatar(byte[] picture, string contentType, CancellationToken cancellationToken);

    Task<CurrentUserDto> RemoveAvatar(CancellationToken cancellationToken);
}
