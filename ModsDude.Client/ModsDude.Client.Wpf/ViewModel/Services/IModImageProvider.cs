using ModsDude.Client.Core.Models;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.ViewModel.Services;

public interface IModImageProvider
{
    /// <summary>Edge length used for the icon in a mod list row.</summary>
    public const int ThumbnailSize = 64;

    /// <summary>Edge length used for the image strip in the mod details dialog.</summary>
    public const int PreviewSize = 96;

    /// <summary>Pass as <c>maxWidth</c> to decode at the image's own resolution.</summary>
    public const int FullSize = 0;


    /// <summary>
    /// Reads and decodes an image, downscaling it to <paramref name="maxWidth"/>. Returns null if
    /// the image can't be read or decoded - a mod with a broken icon shouldn't take the page down.
    /// Results at thumbnail sizes are cached, so calling this repeatedly for the same image is cheap.
    /// </summary>
    Task<ImageSource?> GetAsync(ModImage image, int maxWidth, CancellationToken cancellationToken);
}
