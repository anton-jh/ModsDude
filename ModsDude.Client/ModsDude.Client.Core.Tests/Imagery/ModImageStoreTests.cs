using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Persistence;
using System.Text;

namespace ModsDude.Client.Core.Tests.Imagery;

public class ModImageStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"modsdude-image-store-{Guid.NewGuid():N}");
    private readonly ModImageCache _cache;


    public ModImageStoreTests()
    {
        _cache = new ModImageCache(() => new ImageCacheSettings() { Path = _directory, MaxSizeBytes = 1024 * 1024 });
    }


    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }


    [Fact]
    public async Task Bytes_that_hash_to_the_address_they_came_from_are_served_and_kept()
    {
        var bytes = Encoding.UTF8.GetBytes("a thumbnail");
        var hash = ModImageHashing.Compute(bytes);
        var client = new FakeImagesClient(bytes);

        var store = new ModImageStore(client, _cache);

        Assert.Equal(bytes, await store.GetAsync(hash, CancellationToken.None));
        Assert.Equal(bytes, await _cache.TryReadAsync(hash, CancellationToken.None));
    }

    [Fact]
    public async Task An_address_is_only_fetched_once_per_machine()
    {
        var bytes = Encoding.UTF8.GetBytes("a thumbnail");
        var hash = ModImageHashing.Compute(bytes);
        var client = new FakeImagesClient(bytes);

        var store = new ModImageStore(client, _cache);

        await store.GetAsync(hash, CancellationToken.None);
        await new ModImageStore(client, _cache).GetAsync(hash, CancellationToken.None);

        Assert.Equal(1, client.Downloads);
    }

    [Fact]
    public async Task Bytes_that_do_not_hash_to_their_address_are_refused()
    {
        var hash = ModImageHashing.Compute(Encoding.UTF8.GetBytes("a thumbnail"));
        var client = new FakeImagesClient(Encoding.UTF8.GetBytes("something else entirely"));

        var store = new ModImageStore(client, _cache);

        await Assert.ThrowsAsync<ModImageVerificationException>(() => store.GetAsync(hash, CancellationToken.None));
    }

    [Fact]
    public async Task Nothing_unverified_reaches_the_cache()
    {
        // The cache is keyed by hash and never re-derives, so bytes that get in wrong stay wrong on
        // this machine forever - which is why the check happens before the write and not after.
        var hash = ModImageHashing.Compute(Encoding.UTF8.GetBytes("a thumbnail"));
        var client = new FakeImagesClient(Encoding.UTF8.GetBytes("something else entirely"));

        var store = new ModImageStore(client, _cache);

        await Assert.ThrowsAsync<ModImageVerificationException>(() => store.GetAsync(hash, CancellationToken.None));

        Assert.Null(await _cache.TryReadAsync(hash, CancellationToken.None));
    }

    [Fact]
    public async Task Anything_that_is_not_an_address_never_reaches_the_server()
    {
        var client = new FakeImagesClient([]);

        var store = new ModImageStore(client, _cache);

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("../../etc/passwd", CancellationToken.None));
        Assert.Equal(0, client.Downloads);
    }


    private class FakeImagesClient(byte[] served) : IImagesClient
    {
        public int Downloads { get; private set; }


        public Task<FileResponse> GetImageV1Async(string hash, CancellationToken cancellationToken = default)
        {
            Downloads++;

            return Task.FromResult(new FileResponse(
                200,
                new Dictionary<string, IEnumerable<string>>(),
                new MemoryStream(served),
                null,
                null));
        }

        public Task<CheckImagesExistResponse> CheckImagesExistV1Async(CheckImagesExistRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CheckImagesExistResponse() { Present = [] });
        }

        public Task UploadImageV1Async(string hash, FileParameter? image = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
