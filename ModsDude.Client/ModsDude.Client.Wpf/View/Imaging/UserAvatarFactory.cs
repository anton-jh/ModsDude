using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Users;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.View.Imaging;

/// <inheritdoc cref="IUserAvatarFactory"/>
/// <remarks>
/// A picture is an address in the same image store mod art lives in, so it rides the same path all
/// the way to the screen: the store's disk cache and hash check, and the provider's decode and
/// in-memory cache. A picture shown in three lists is fetched once and decoded once.
/// </remarks>
public class UserAvatarFactory(
    IModImageStore store,
    IModImageProvider provider)
    : IUserAvatarFactory
{
    /// <summary>
    /// Enough for the largest circle a picture is drawn in, and small enough that the provider keeps
    /// the decoded image in memory rather than decoding it again for every list.
    /// </summary>
    private const int _drawnSize = ModImageRenditions.ThumbnailMaxEdge;


    public AvatarViewModel Create(string tag, string displayName, string? avatarHash)
    {
        var avatar = new AvatarViewModel(UserDisplay.ColorFor(tag), UserDisplay.InitialFor(displayName));

        if (ModImageHashing.IsValidHash(avatarHash))
        {
            _ = LoadAsync(avatar, avatarHash!);
        }

        return avatar;
    }


    private async Task LoadAsync(AvatarViewModel avatar, string hash)
    {
        var image = new ModImage("avatar", hash, ct => store.GetAsync(hash, ct)) { IsPreSized = true };

        // Never throws: the provider logs and counts a failure and hands back null, which leaves the
        // initial showing.
        avatar.Image = await provider.GetAsync(image, _drawnSize, CancellationToken.None);
    }
}
