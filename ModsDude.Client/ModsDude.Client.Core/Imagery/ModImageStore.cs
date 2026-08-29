using ModsDude.Client.Core.ModsDudeServer.Generated;
using System.Collections.Concurrent;

namespace ModsDude.Client.Core.Imagery;

/// <summary>
/// Reads a derivative by its address, from the machine cache when it is there and from the server
/// otherwise.
/// </summary>
public interface IModImageStore
{
    Task<byte[]> GetAsync(string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Puts bytes the caller just derived at their own address, so the client that generated them
    /// draws them without asking for them back.
    /// </summary>
    Task PutAsync(string hash, byte[] bytes, CancellationToken cancellationToken);
}


/// <inheritdoc cref="IModImageStore"/>
public class ModImageStore(
    IImagesClient imagesClient,
    ModImageCache cache)
    : IModImageStore
{
    /// <summary>
    /// A list of 540 rows realizes its icons in bursts, and the same artwork is shared across
    /// versions of a mod, so the same address is asked for several times at once.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _inFlight = new();


    public Task<byte[]> GetAsync(string hash, CancellationToken cancellationToken)
    {
        if (ModImageHashing.IsValidHash(hash) is false)
        {
            throw new ArgumentException($"'{hash}' is not an image address.", nameof(hash));
        }

        // Deliberately not passing the caller's token: the fetch is shared, so one row scrolling out
        // of view must not cancel it for everyone else waiting on the same address.
        var load = _inFlight.GetOrAdd(hash, _ => new Lazy<Task<byte[]>>(() => LoadAsync(hash)));

        return load.Value;
    }

    public async Task PutAsync(string hash, byte[] bytes, CancellationToken cancellationToken)
    {
        await cache.WriteAsync(hash, bytes, cancellationToken);
    }


    private async Task<byte[]> LoadAsync(string hash)
    {
        try
        {
            if (await cache.TryReadAsync(hash, CancellationToken.None) is byte[] cached)
            {
                return cached;
            }

            var downloaded = await DownloadAsync(hash);

            // Verified before it reaches the cache, never after. The cache is keyed by hash and
            // never re-derives, so bytes that get in wrong stay wrong on this machine forever.
            if (ModImageHashing.Verify(hash, downloaded) is false)
            {
                throw new ModImageVerificationException(hash);
            }

            await cache.WriteAsync(hash, downloaded, CancellationToken.None);

            return downloaded;
        }
        finally
        {
            // Held only for the duration of the fetch. Keeping the task would keep every image the
            // app has ever drawn in memory, which is what the disk cache is for.
            _inFlight.TryRemove(hash, out _);
        }
    }

    private async Task<byte[]> DownloadAsync(string hash)
    {
        using var response = await imagesClient.GetImageV1Async(hash, CancellationToken.None);
        using var buffer = new MemoryStream();

        await response.Stream.CopyToAsync(buffer, CancellationToken.None);

        return buffer.ToArray();
    }
}


public class ModImageVerificationException(string hash)
    : Exception($"The bytes served for image '{hash}' do not hash to that address.")
{
    public string Hash { get; } = hash;
}
