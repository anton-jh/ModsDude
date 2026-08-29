using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModsDude.Client.Wpf.View.Imaging;

/// <inheritdoc cref="IModImageProvider"/>
public class ModImageProvider(ModImageCache cache) : IModImageProvider, IDisposable
{
    /// <summary>
    /// Images at or below this width are small enough to keep around - a thousand of them is a
    /// handful of megabytes. Anything larger is loaded on demand and dropped again.
    /// </summary>
    private const int _maxCachedWidth = ModImageDerivatives.ThumbnailMaxEdge;

    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _cache = new();

    /// <summary>
    /// Scrolling a long list fast queues up far more work than it consumes. Bounding it keeps the
    /// machine responsive and stops a burst of decodes from starving everything else.
    /// </summary>
    private readonly SemaphoreSlim _throttle = new(Math.Max(2, Environment.ProcessorCount / 2));


    public Task<ImageSource?> GetAsync(ModImage image, int maxWidth, CancellationToken cancellationToken)
    {
        var wantsFullSize = maxWidth == IModImageProvider.FullSize || maxWidth > _maxCachedWidth;

        // A server-backed image is stored as two renditions, and asking for the larger one is what
        // opening an image to look at it means. A local image is loaded whole either way.
        var source = wantsFullSize
            ? image.FullSize ?? image
            : image;

        if (wantsFullSize)
        {
            return LoadAsync(source, maxWidth, cancellationToken);
        }

        var key = $"{source.CacheKey}|{maxWidth}";

        // Deliberately not passing the caller's token: the task is shared between everyone asking
        // for this image, so one row scrolling out of view must not cancel it for the others.
        return _cache
            .GetOrAdd(key, _ => new Lazy<Task<ImageSource?>>(() => LoadThroughDiskCacheAsync(source, maxWidth, key)))
            .Value;
    }

    public void Dispose()
    {
        _throttle.Dispose();
        GC.SuppressFinalize(this);
    }


    private async Task<ImageSource?> LoadThroughDiskCacheAsync(ModImage image, int maxWidth, string key)
    {
        // A derivative is already the size it will be drawn at and already sits in the cache under
        // its own address, so re-deriving it would only store the same picture twice under a key
        // that can invalidate.
        if (image.IsPreSized)
        {
            return await LoadAsync(image, maxWidth, CancellationToken.None);
        }

        if (await cache.TryReadAsync(key, CancellationToken.None) is byte[] cached
            && TryDecodeCached(cached) is ImageSource decodedFromCache)
        {
            return decodedFromCache;
        }

        var decoded = await LoadAsync(image, maxWidth, CancellationToken.None);

        if (decoded is BitmapSource bitmap)
        {
            _ = Task.Run(() => WriteToCacheAsync(key, bitmap));
        }

        return decoded;
    }

    private async Task<ImageSource?> LoadAsync(ModImage image, int maxWidth, CancellationToken cancellationToken)
    {
        try
        {
            await _throttle.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            var data = await image.Load(cancellationToken);

            return await Task.Run(() => ModImageDecoder.Decode(data, maxWidth), cancellationToken);
        }
        catch (Exception)
        {
            // A mod with an unreadable image just shows the placeholder. Nothing here is worth
            // interrupting the user over. That covers a derivative whose bytes did not hash to the
            // address they came from, which is refused rather than drawn.
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static ImageSource? TryDecodeCached(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);

            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();

            return frame;
        }
        catch (Exception)
        {
            // A corrupt or half-written cache entry is not worth reporting - decode from the mod.
            return null;
        }
    }

    private async Task WriteToCacheAsync(string key, BitmapSource bitmap)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var buffer = new MemoryStream();
            encoder.Save(buffer);

            await cache.WriteAsync(key, buffer.ToArray(), CancellationToken.None);
        }
        catch (Exception)
        {
            // The cache is an optimization. Losing a write costs a re-decode, nothing more.
        }
    }
}
