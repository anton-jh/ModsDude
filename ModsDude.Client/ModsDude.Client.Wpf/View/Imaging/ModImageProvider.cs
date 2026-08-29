using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModsDude.Client.Wpf.View.Imaging;

/// <inheritdoc cref="IModImageProvider"/>
public class ModImageProvider : IModImageProvider, IDisposable
{
    /// <summary>
    /// Images at or below this width are small enough to keep around - a thousand of them is a
    /// handful of megabytes. Anything larger is loaded on demand and dropped again.
    /// </summary>
    private const int _maxCachedWidth = 128;

    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _cache = new();

    /// <summary>
    /// Scrolling a long list fast queues up far more work than it consumes. Bounding it keeps the
    /// machine responsive and stops a burst of decodes from starving everything else.
    /// </summary>
    private readonly SemaphoreSlim _throttle = new(Math.Max(2, Environment.ProcessorCount / 2));

    private readonly string _diskCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModsDude",
        "image-cache");


    public Task<ImageSource?> GetAsync(ModImage image, int maxWidth, CancellationToken cancellationToken)
    {
        if (maxWidth > _maxCachedWidth || maxWidth == IModImageProvider.FullSize)
        {
            return LoadAsync(image, maxWidth, cancellationToken);
        }

        var key = $"{image.CacheKey}|{maxWidth}";

        // Deliberately not passing the caller's token: the task is shared between everyone asking
        // for this image, so one row scrolling out of view must not cancel it for the others.
        return _cache
            .GetOrAdd(key, _ => new Lazy<Task<ImageSource?>>(() => LoadThroughDiskCacheAsync(image, maxWidth, key)))
            .Value;
    }

    public void Dispose()
    {
        _throttle.Dispose();
        GC.SuppressFinalize(this);
    }


    private async Task<ImageSource?> LoadThroughDiskCacheAsync(ModImage image, int maxWidth, string key)
    {
        var path = GetDiskCachePath(key);

        var cached = await Task.Run(() => TryReadFromDisk(path));
        if (cached is not null)
        {
            return cached;
        }

        var decoded = await LoadAsync(image, maxWidth, CancellationToken.None);

        if (decoded is BitmapSource bitmap)
        {
            _ = Task.Run(() => TryWriteToDisk(path, bitmap));
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
            // interrupting the user over.
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private string GetDiskCachePath(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var name = Convert.ToHexString(hash, 0, 10);

        return Path.Combine(_diskCacheDirectory, $"{name}.png");
    }

    private static ImageSource? TryReadFromDisk(string path)
    {
        try
        {
            if (File.Exists(path) is false)
            {
                return null;
            }

            using var stream = new MemoryStream(File.ReadAllBytes(path));

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

    private void TryWriteToDisk(string path, BitmapSource bitmap)
    {
        try
        {
            Directory.CreateDirectory(_diskCacheDirectory);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var temporaryPath = $"{path}.{Environment.CurrentManagedThreadId}.tmp";

            using (var stream = File.Create(temporaryPath))
            {
                encoder.Save(stream);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception)
        {
            // The cache is an optimization. Losing a write costs a re-decode, nothing more.
        }
    }
}
