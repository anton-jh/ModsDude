using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Sync;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace ModsDude.Client.Core.Tests.Sync;

public class HttpModFileDownloaderTests
{
    private const int _chunk = 1000;


    [Fact]
    public async Task A_file_no_larger_than_a_chunk_is_one_request()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk));

        using var download = await Open(storage);

        Assert.Equal(storage.Blob, await ReadAll(download));
        Assert.Equal(_chunk, download.Length);
        Assert.Single(storage.Requests);
    }

    [Fact]
    public async Task A_large_file_arrives_whole_and_in_order()
    {
        // Not a whole number of chunks, so the last one is short.
        var storage = new FakeBlobStorage(Bytes(_chunk * 17 + 123));

        using var download = await Open(storage);

        Assert.Equal(storage.Blob, await ReadAll(download));
        Assert.Equal(storage.Blob.Length, download.Length);
        Assert.Equal(18, storage.Requests.Count);
    }

    [Fact]
    public async Task Every_range_after_the_first_is_pinned_to_the_first_answers_version()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 5));

        using var download = await Open(storage);
        await ReadAll(download);

        Assert.All(storage.Requests.Skip(1), x => Assert.Equal(storage.ETag, x.IfMatch));
    }

    [Fact]
    public async Task No_more_than_the_per_file_connections_are_open_at_once()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 40)) { Latency = TimeSpan.FromMilliseconds(20) };

        using var download = await Open(storage);
        await ReadAll(download);

        // The opening request is outside the count, so it can make one more.
        Assert.InRange(storage.MaxConcurrent, 2, Options().ConnectionsPerFile + 1);
    }

    [Fact]
    public async Task A_failed_chunk_is_retried_on_its_own()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 6))
        {
            Faults = (offset, attempt) => offset == _chunk * 3 && attempt == 1 ? Fault.ServerError : Fault.None
        };

        using var download = await Open(storage);

        Assert.Equal(storage.Blob, await ReadAll(download));
        Assert.Equal(2, storage.Requests.Count(x => x.Offset == _chunk * 3));
    }

    [Fact]
    public async Task A_connection_that_drops_mid_chunk_is_retried()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 6))
        {
            Faults = (offset, attempt) => offset == _chunk * 2 && attempt <= 2 ? Fault.Truncate : Fault.None
        };

        using var download = await Open(storage);

        Assert.Equal(storage.Blob, await ReadAll(download));
    }

    [Fact]
    public async Task A_dropped_opening_request_is_re_requested_rather_than_failing()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 3))
        {
            Faults = (offset, attempt) => offset == 0 && attempt == 1 ? Fault.Truncate : Fault.None
        };

        using var download = await Open(storage);

        Assert.Equal(storage.Blob, await ReadAll(download));
    }

    [Fact]
    public async Task A_connection_that_stalls_is_abandoned_and_its_range_re_requested()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 4))
        {
            Faults = (offset, attempt) => offset == _chunk && attempt == 1 ? Fault.Stall : Fault.None
        };

        using var download = await Open(storage, Options() with { StallTimeout = TimeSpan.FromMilliseconds(200) });

        Assert.Equal(storage.Blob, await ReadAll(download));
    }

    [Fact]
    public async Task A_chunk_that_keeps_failing_fails_the_download()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 4))
        {
            Faults = (offset, _) => offset == _chunk * 2 ? Fault.ServerError : Fault.None
        };

        using var download = await Open(storage);

        await Assert.ThrowsAsync<HttpRequestException>(() => ReadAll(download));
        Assert.Equal(Options().MaxAttempts, storage.Requests.Count(x => x.Offset == _chunk * 2));
    }

    [Fact]
    public async Task A_blob_replaced_mid_download_fails_rather_than_being_spliced()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 4))
        {
            Faults = (offset, _) => offset == _chunk * 2 ? Fault.PreconditionFailed : Fault.None
        };

        using var download = await Open(storage);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => ReadAll(download));

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Single(storage.Requests, x => x.Offset == _chunk * 2);
    }

    [Fact]
    public async Task A_server_that_ignores_ranges_is_read_as_one_stream()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 5)) { IgnoresRange = true };

        using var download = await Open(storage);

        Assert.Equal(storage.Blob, await ReadAll(download));
        Assert.Single(storage.Requests);
    }

    [Fact]
    public async Task An_empty_blob_downloads_as_nothing()
    {
        var storage = new FakeBlobStorage([]);

        using var download = await Open(storage);

        Assert.Empty(await ReadAll(download));
    }

    [Fact]
    public async Task A_ranged_download_ingests_into_the_store_at_its_address()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 9 + 7));
        var hash = ModContentHasher.Format(System.Security.Cryptography.SHA256.HashData(storage.Blob));
        var root = Path.Combine(Path.GetTempPath(), $"modsdude-ranged-{Guid.NewGuid():N}");

        try
        {
            var store = new ContentStore(Path.GetPathRoot(root)!, root, long.MaxValue);
            var reported = new List<long>();

            using (var download = await Open(storage))
            {
                await store.IngestAsync(download.Content, hash, new SynchronousProgress(reported.Add), CancellationToken.None);
            }

            Assert.Equal(storage.Blob, await File.ReadAllBytesAsync(store.GetBlobPath(hash)));
            Assert.Equal(storage.Blob.Length, reported[^1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Disposing_part_way_through_stops_fetching()
    {
        var storage = new FakeBlobStorage(Bytes(_chunk * 200)) { Latency = TimeSpan.FromMilliseconds(10) };

        var download = await Open(storage);
        await download.Content.ReadExactlyAsync(new byte[_chunk * 2]);
        download.Dispose();

        await Task.Delay(300);
        var settled = storage.Requests.Count;
        await Task.Delay(300);

        Assert.Equal(settled, storage.Requests.Count);
        Assert.True(settled < 200);
    }


    [Fact]
    public async Task Bytes_received_run_ahead_of_the_reader_and_never_backwards()
    {
        // Chunks big enough that the window holds a few megabytes, which is the reporting step.
        var options = Options() with { ChunkSize = 512 * 1024 };
        var storage = new FakeBlobStorage(Bytes(options.ChunkSize * 12 + 99))
        {
            Faults = (offset, attempt) => offset == 512 * 1024 * 3 && attempt == 1 ? Fault.Truncate : Fault.None
        };
        var reported = new ConcurrentQueue<long>();

        using var download = await Open(storage, options, new SynchronousProgress(reported.Enqueue));

        // Nothing read yet, and still the bar moves: the window is filling behind the reader.
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (reported.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.False(reported.IsEmpty);

        await ReadAll(download);

        var values = reported.ToArray();

        Assert.Equal(storage.Blob.Length, values[^1]);
        Assert.All(values.Zip(values.Skip(1)), x => Assert.True(x.Second > x.First));
    }

    private static RangedDownloadOptions Options() => new()
    {
        ChunkSize = _chunk,
        ConnectionsPerFile = 3,
        WindowChunks = 5,
        MaxAttempts = 3,
        RetryDelay = TimeSpan.Zero,
        ConnectionBudget = new SemaphoreSlim(16)
    };

    private static Task<ModFileDownload> Open(FakeBlobStorage storage, RangedDownloadOptions? options = null, IProgress<long>? bytesReceived = null)
    {
        var downloader = new HttpModFileDownloader(new HttpClient(storage), options ?? Options());

        return downloader.OpenAsync("https://storage.example/blob?sas", bytesReceived, CancellationToken.None);
    }

    private static async Task<byte[]> ReadAll(ModFileDownload download)
    {
        var buffer = new MemoryStream();
        await download.Content.CopyToAsync(buffer, 777);

        return buffer.ToArray();
    }

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);

        return bytes;
    }


    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    private enum Fault { None, ServerError, Truncate, Stall, PreconditionFailed }

    private sealed record Request(long? Offset, EntityTagHeaderValue? IfMatch);

    /// <summary>Blob storage's side of a GET: ranges, ETags and If-Match, and faults on demand.</summary>
    private sealed class FakeBlobStorage(byte[] blob) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<long, int> _attempts = new();
        private int _concurrent;
        private int _maxConcurrent;


        public byte[] Blob { get; } = blob;
        public EntityTagHeaderValue ETag { get; } = new("\"0x8DC\"");
        public ConcurrentQueue<Request> Requests { get; } = new();
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public bool IgnoresRange { get; init; }
        public TimeSpan Latency { get; init; }

        /// <summary>Given the range's offset and which attempt at it this is, from one.</summary>
        public Func<long, int, Fault> Faults { get; init; } = (_, _) => Fault.None;


        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var range = request.Headers.Range?.Ranges.Single();
            var ifMatch = request.Headers.IfMatch.SingleOrDefault();

            Requests.Enqueue(new Request(range?.From, ifMatch));

            var concurrent = Interlocked.Increment(ref _concurrent);
            InterlockedMax(ref _maxConcurrent, concurrent);

            try
            {
                if (Latency > TimeSpan.Zero)
                {
                    await Task.Delay(Latency, cancellationToken);
                }

                var offset = range?.From ?? 0;
                var fault = Faults(offset, _attempts.AddOrUpdate(offset, 1, (_, x) => x + 1));

                switch (fault)
                {
                    case Fault.ServerError:
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("<Error>busy</Error>") };
                    case Fault.PreconditionFailed:
                        return new HttpResponseMessage(HttpStatusCode.PreconditionFailed) { Content = new StringContent("<Error>changed</Error>") };
                    case Fault.Stall:
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                        break;
                }

                if (range is null || IgnoresRange)
                {
                    return Respond(HttpStatusCode.OK, Blob, null);
                }

                if (Blob.Length == 0 || range.From >= Blob.Length)
                {
                    return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) { Content = new StringContent("") };
                }

                var from = range.From!.Value;
                var to = Math.Min(range.To ?? long.MaxValue, Blob.Length - 1);
                var body = Blob[(int)from..(int)(to + 1)];

                if (fault is Fault.Truncate)
                {
                    body = body[..(body.Length / 2)];
                }

                return Respond(HttpStatusCode.PartialContent, body, new ContentRangeHeaderValue(from, to, Blob.Length));
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        private HttpResponseMessage Respond(HttpStatusCode status, byte[] body, ContentRangeHeaderValue? contentRange)
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            response.Headers.ETag = ETag;
            response.Content.Headers.ContentRange = contentRange;

            return response;
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;

            while ((current = Volatile.Read(ref target)) < value &&
                   Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
