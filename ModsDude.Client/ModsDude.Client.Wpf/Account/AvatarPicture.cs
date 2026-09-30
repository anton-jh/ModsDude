using ModsDude.Client.Wpf.Mods.Imaging;
using ModsDude.Client.Wpf.Shared;

namespace ModsDude.Client.Wpf.Account;

/// <summary>
/// Turns a picture the user picked into the one that is stored: the largest centred square of it,
/// no bigger than <see cref="Size"/>, as WebP.
/// </summary>
/// <remarks>
/// Made here for the reason mod derivatives are: the server has no image stack, so whatever it is
/// sent is what every teammate downloads. A phone photo straight off the camera is several megabytes
/// to draw a 28 px circle; this is a few kilobytes, and the same size wherever it came from.
/// </remarks>
internal static class AvatarPicture
{
    /// <summary>Twice the largest circle one is drawn in, for a sharp result on a high-DPI screen.</summary>
    public const int Size = 256;

    public const string ContentType = "image/webp";


    /// <exception cref="Exception">Whatever the decoder throws for bytes that are not a picture it can read.</exception>
    public static byte[] Make(byte[] data)
    {
        var decoded = ModImageDecoder.DecodeToPixels(data);

        var edge = Math.Min(decoded.Width, decoded.Height);
        var left = (decoded.Width - edge) / 2;
        var top = (decoded.Height - edge) / 2;

        var square = new byte[edge * edge * 4];
        var sourceStride = decoded.Width * 4;
        var squareStride = edge * 4;

        for (var row = 0; row < edge; row++)
        {
            Buffer.BlockCopy(decoded.Bgra, (top + row) * sourceStride + left * 4, square, row * squareStride, squareStride);
        }

        var target = Math.Min(edge, Size);

        return WebPCodec.Encode(square, edge, edge, target, target);
    }
}
