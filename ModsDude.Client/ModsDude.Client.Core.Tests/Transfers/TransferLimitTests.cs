using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Transfers;
using System.Diagnostics;
using System.Net;

namespace ModsDude.Client.Core.Tests.Transfers;

public class TransferLimitTests
{
    private const int _megabyte = 1024 * 1024;


    [Fact]
    public void Without_a_limit_nothing_ever_waits()
    {
        var limiter = new TransferRateLimiter();

        for (var i = 0; i < 1000; i++)
        {
            Assert.True(limiter.ConsumeAsync(_megabyte, CancellationToken.None).IsCompleted);
        }

        Assert.Equal(int.MaxValue, limiter.ConnectionCap);
    }

    [Fact]
    public async Task A_limit_holds_the_rate_to_it()
    {
        var limiter = new TransferRateLimiter { BytesPerSecond = 2 * _megabyte };
        var stopwatch = Stopwatch.StartNew();

        // 2 MB at 2 MB/s, less the quarter second of burst the bucket may start with.
        for (var i = 0; i < 32; i++)
        {
            await limiter.ConsumeAsync(64 * 1024, CancellationToken.None);
        }

        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0.6, 2.5);
    }

    [Fact]
    public async Task Concurrent_transfers_share_one_limit()
    {
        var limiter = new TransferRateLimiter { BytesPerSecond = 2 * _megabyte };
        var stopwatch = Stopwatch.StartNew();

        // Four at 512 KB each is the same 2 MB, and so the same second, as one at 2 MB.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            for (var i = 0; i < 8; i++)
            {
                await limiter.ConsumeAsync(64 * 1024, CancellationToken.None);
            }
        }));

        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0.6, 2.5);
    }

    [Fact]
    public async Task Lifting_a_limit_frees_what_it_was_holding_back()
    {
        var limiter = new TransferRateLimiter { BytesPerSecond = 256 * 1024 };

        using var first = await limiter.AcquireConnectionAsync(CancellationToken.None);
        var second = limiter.AcquireConnectionAsync(CancellationToken.None).AsTask();

        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        limiter.BytesPerSecond = null;

        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task A_connection_handed_back_lets_the_next_one_in()
    {
        var limiter = new TransferRateLimiter { BytesPerSecond = 256 * 1024 };

        var first = await limiter.AcquireConnectionAsync(CancellationToken.None);
        var second = limiter.AcquireConnectionAsync(CancellationToken.None).AsTask();

        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();

        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public void A_limit_allows_as_many_connections_as_it_can_keep_busy()
    {
        var limiter = new TransferRateLimiter { BytesPerSecond = 100 * 1024 };
        Assert.Equal(1, limiter.ConnectionCap);

        limiter.BytesPerSecond = 2 * _megabyte;
        Assert.Equal(8, limiter.ConnectionCap);
    }

    [Fact]
    public void Settings_reach_the_limiters_and_say_so()
    {
        var limits = new TransferLimits();
        var changes = 0;
        limits.Changed += (_, _) => changes++;

        limits.Apply(new TransferLimitSettings { DownloadBytesPerSecond = 1000, UploadBytesPerSecond = null });

        Assert.Equal(1000, limits.Download.BytesPerSecond);
        Assert.Null(limits.Upload.BytesPerSecond);
        Assert.Equal(1, changes);

        // The same again is not a change.
        limits.Apply(new TransferLimitSettings { DownloadBytesPerSecond = 1000 });
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task An_upload_is_held_to_the_upload_limit()
    {
        var limits = TransferLimits.From(new TransferLimitSettings { UploadBytesPerSecond = 2 * _megabyte });
        var storage = new SinkStorage();
        var uploader = new BlockBlobModFileUploader(new HttpClient(storage), limits);
        var bytes = new byte[2 * _megabyte];
        var reported = new List<long>();

        var stopwatch = Stopwatch.StartNew();

        await uploader.UploadAsync(
            new ModFileUpload("https://storage.example/blob?sas", "contenthash", () => new MemoryStream(bytes))
            {
                BytesTransferred = new SynchronousProgress(reported.Add)
            },
            CancellationToken.None);

        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0.6, 3);
        Assert.Equal(bytes.Length, storage.BlockBytes);

        // Part way through the one block as well as at its end, so a slow bar still moves.
        Assert.True(reported.Count > 1);
        Assert.Equal(bytes.Length, reported[^1]);
    }


    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    /// <summary>Accepts every block and commit, and counts what the blocks carried.</summary>
    private sealed class SinkStorage : HttpMessageHandler
    {
        public long BlockBytes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);

            if (request.RequestUri!.Query.Contains("comp=block&"))
            {
                BlockBytes += body.Length;
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }
}
