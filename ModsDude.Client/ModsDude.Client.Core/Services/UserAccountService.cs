using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Services;

/// <summary>
/// The signed-in user's changes to how they appear to everybody else: their name and their picture.
/// Each answers with the user as the server now has them.
/// </summary>
/// <remarks>
/// The password is not here and cannot be: it belongs to the identity provider, which is where the
/// account page sends the user to reset it.
/// </remarks>
public class UserAccountService(
    IUsersClient usersClient,
    IImagesClient imagesClient,
    IModImageStore imageStore) : IUserAccountService
{
    public Task<CurrentUserDto> SetDisplayName(string displayName, CancellationToken cancellationToken)
    {
        return usersClient.SetDisplayNameV1Async(new SetDisplayNameRequest { DisplayName = displayName }, cancellationToken);
    }

    public async Task<CurrentUserDto> SetAvatar(byte[] picture, string contentType, CancellationToken cancellationToken)
    {
        var hash = ModImageHashing.Compute(picture);

        var present = await imagesClient.CheckImagesExistV1Async(new CheckImagesExistRequest { Hashes = [hash] }, cancellationToken);

        if (present.Present.Contains(hash) is false)
        {
            using var content = new MemoryStream(picture);

            await imagesClient.UploadImageV1Async(hash, new FileParameter(content, hash, contentType), cancellationToken);
        }

        var user = await usersClient.SetAvatarV1Async(new SetAvatarRequest { Hash = hash }, cancellationToken);

        // The client that made the picture is the first to draw it, and it already holds the bytes.
        await imageStore.PutAsync(hash, picture, cancellationToken);

        return user;
    }

    public Task<CurrentUserDto> RemoveAvatar(CancellationToken cancellationToken)
    {
        return usersClient.RemoveAvatarV1Async(cancellationToken);
    }
}
