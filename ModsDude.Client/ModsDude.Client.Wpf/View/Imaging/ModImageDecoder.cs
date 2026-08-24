using BCnEncoder.Decoder;
using BCnEncoder.Shared.ImageFiles;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModsDude.Client.Wpf.View.Imaging;

/// <summary>
/// Turns raw mod image bytes into something WPF can show.
/// </summary>
/// <remarks>
/// Mod images are almost exclusively DDS. Windows ships a WIC codec for it, which covers the
/// legacy FourCC formats - every mod icon observed so far is DXT1 - but it refuses BC7, which is
/// what the majority of store images use. Those get decoded in managed code instead.
/// </remarks>
internal static class ModImageDecoder
{
    private static readonly BcDecoder _blockDecoder = new();


    public static ImageSource Decode(byte[] data, int maxWidth)
    {
        try
        {
            return DecodeWithWindowsCodecs(data, maxWidth);
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or COMException or ArgumentException or OverflowException)
        {
            return DecodeBlockCompressed(data, maxWidth);
        }
    }

    private static ImageSource DecodeWithWindowsCodecs(byte[] data, int maxWidth)
    {
        // WIC needs to seek, and the callers hand us bytes read out of a zip entry.
        using var stream = new MemoryStream(data);

        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        return Resize(decoder.Frames[0], maxWidth);
    }

    private static ImageSource DecodeBlockCompressed(byte[] data, int maxWidth)
    {
        using var stream = new MemoryStream(data);

        var file = DdsFile.Load(stream);
        var face = file.Faces[0];
        var width = (int)face.Width;
        var height = (int)face.Height;

        var pixels = _blockDecoder.DecodeRaw(face.MipMaps[0].Data, width, height, _blockDecoder.GetFormat(file));

        var bgra = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            bgra[(i * 4) + 0] = pixels[i].b;
            bgra[(i * 4) + 1] = pixels[i].g;
            bgra[(i * 4) + 2] = pixels[i].r;
            bgra[(i * 4) + 3] = pixels[i].a;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);

        return Resize(bitmap, maxWidth);
    }

    private static ImageSource Resize(BitmapSource source, int maxWidth)
    {
        source.Freeze();

        if (maxWidth <= 0 || source.PixelWidth <= maxWidth)
        {
            return Materialize(source);
        }

        var scale = maxWidth / (double)source.PixelWidth;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();

        return Materialize(scaled);
    }

    /// <summary>
    /// Copies the pixels into a standalone bitmap. A <see cref="TransformedBitmap"/> keeps its
    /// full-resolution source alive, which for a list of a thousand 512x512 icons is the
    /// difference between roughly 16 MB of thumbnails and a gigabyte of originals.
    /// </summary>
    private static ImageSource Materialize(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var result = BitmapSource.Create(
            converted.PixelWidth, converted.PixelHeight,
            96, 96,
            PixelFormats.Bgra32, null,
            pixels, stride);

        result.Freeze();

        return result;
    }
}
