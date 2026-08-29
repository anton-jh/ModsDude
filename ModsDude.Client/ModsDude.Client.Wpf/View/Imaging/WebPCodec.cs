using SkiaSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModsDude.Client.Wpf.View.Imaging;

/// <summary>
/// The WebP end of the imagery pipeline, on Skia.
/// </summary>
/// <remarks>
/// WPF has no WebP encoder at all, and its decoder is not dependable either: WIC only reads WebP
/// where the optional Windows image extension is installed, which is not something a mod list can
/// be allowed to depend on. Both directions therefore go through one codec that ships with the app.
/// </remarks>
internal static class WebPCodec
{
    /// <summary>
    /// High enough that a re-encoded 512 px store image is indistinguishable at the sizes it is
    /// drawn, low enough to hit the ~6 KB thumbnail and ~50 KB full the transfer argument is built
    /// on.
    /// </summary>
    private const int _quality = 80;


    public static bool IsWebP(ReadOnlySpan<byte> data)
    {
        return data.Length >= 12
            && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P';
    }

    public static BitmapSource Decode(byte[] data)
    {
        var bounds = SKBitmap.DecodeBounds(data);
        var info = new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        using var bitmap = SKBitmap.Decode(data, info)
            ?? throw new FileFormatException(new Uri("about:blank"), "The bytes are not a readable WebP image.");

        var bitmapSource = BitmapSource.Create(
            info.Width, info.Height,
            96, 96,
            PixelFormats.Bgra32, null,
            bitmap.GetPixelSpan().ToArray(), info.Width * 4);

        bitmapSource.Freeze();

        return bitmapSource;
    }

    /// <summary>
    /// Encodes straight, unpremultiplied BGRA - the one pixel layout both WPF's Bgra32 and the
    /// managed block decoder already produce, so nothing on the way in has to be converted.
    /// </summary>
    public static byte[] Encode(byte[] bgra, int width, int height, int targetWidth, int targetHeight)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        using var source = new SKBitmap(info);
        Marshal.Copy(bgra, 0, source.GetPixels(), Math.Min(bgra.Length, info.BytesSize));

        // Refused rather than encoded at the wrong size: a rendition that is not the size its
        // position says it is would be wrong everywhere it is later drawn.
        using var resized = width == targetWidth && height == targetHeight
            ? null
            : source.Resize(
                new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul),
                new SKSamplingOptions(SKCubicResampler.Mitchell))
                ?? throw new InvalidOperationException($"The image could not be resized to {targetWidth}x{targetHeight}.");

        using var encoded = (resized ?? source).Encode(SKEncodedImageFormat.Webp, _quality)
            ?? throw new InvalidOperationException("The image could not be encoded as WebP.");

        return encoded.ToArray();
    }
}
