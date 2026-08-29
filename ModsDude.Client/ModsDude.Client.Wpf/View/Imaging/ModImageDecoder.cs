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
/// what the majority of store images use. Those get decoded in managed code instead. Server
/// derivatives arrive as WebP, which WIC only reads where an optional Windows extension happens to
/// be installed, so they go through the codec the app ships with.
/// </remarks>
internal static class ModImageDecoder
{
    private static readonly BcDecoder _blockDecoder = new();


    public static ImageSource Decode(byte[] data, int maxWidth)
    {
        return Resize(DecodeToBitmap(data), maxWidth);
    }

    /// <summary>
    /// The image at its own resolution, as straight BGRA. What derivative generation needs: the
    /// encoder resizes from the full-resolution pixels rather than from something already reduced.
    /// </summary>
    public static DecodedImage DecodeToPixels(byte[] data)
    {
        var bitmap = DecodeToBitmap(data);

        var converted = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        return new DecodedImage(pixels, converted.PixelWidth, converted.PixelHeight);
    }


    private static BitmapSource DecodeToBitmap(byte[] data)
    {
        if (WebPCodec.IsWebP(data))
        {
            return WebPCodec.Decode(data);
        }

        try
        {
            return DecodeWithWindowsCodecs(data);
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or COMException or ArgumentException or OverflowException)
        {
            return DecodeBlockCompressed(data);
        }
    }

    private static BitmapSource DecodeWithWindowsCodecs(byte[] data)
    {
        // WIC needs to seek, and the callers hand us bytes read out of a zip entry.
        using var stream = new MemoryStream(data);

        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        return decoder.Frames[0];
    }

    private static BitmapSource DecodeBlockCompressed(byte[] data)
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

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
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


/// <param name="Bgra">Straight, unpremultiplied BGRA, one row after another with no padding.</param>
internal record DecodedImage(byte[] Bgra, int Width, int Height);
