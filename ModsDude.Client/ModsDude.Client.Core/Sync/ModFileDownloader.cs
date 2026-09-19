using System.Buffers;
using System.Net;
using System.Net.Http.Headers;

namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Fetches one mod file from the storage link the server minted.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="Import.IModFileUploader"/>, and deliberately as thin: it hands back
/// a stream and knows nothing about hashes. Verification belongs to the store, which is the one
/// place bytes become an address - see <see cref="ContentStore.IngestAsync"/>.
/// </remarks>
public interface IModFileDownloader
{
    /// <param name="link">The read SAS from <c>CreateModDownloadLink</c>.</param>
    /// <param name="bytesReceived">
    /// How much has arrived so far, where that runs ahead of what the stream has handed out - a
    /// ranged download, whose later chunks land while the reader is still waiting on an earlier one.
    /// A single stream never reports here, since its reader's own count is the same number.
    /// </param>
    /// <returns>The blob's contents, and its length where storage declared one.</returns>
    Task<ModFileDownload> OpenAsync(string link, IProgress<long>? bytesReceived, CancellationToken cancellationToken);
}

/// <summary>Disposing this releases the response the stream is reading from.</summary>
public sealed class ModFileDownload(Stream content, long? length, IDisposable response)
    : IDisposable
{
    public Stream Content { get; } = content;

    /// <summary>Null when storage did not say, which is what a chunked response looks like.</summary>
    public long? Length { get; } = length;


    public void Dispose()
    {
        Content.Dispose();
        response.Dispose();
    }
}


/// <summary>How a large download is split up. See docs/07-mod-sync-design.md#downloading.</summary>
public sealed record RangedDownloadOptions
{
    /// <summary>
    /// Every range GET from every download in the process draws from this, so several files fetched
    /// at once share the line rather than multiplying connections.
    /// </summary>
    private static readonly SemaphoreSlim _processConnectionBudget = new(16);


    public static RangedDownloadOptions Default { get; } = new();


    /// <summary>
    /// The size of each range, and of the first request - so a file no larger than this is one
    /// plain request, as it always was. Large enough that a request's round trip is a small part of
    /// the time the connection spends delivering it.
    /// </summary>
    public int ChunkSize { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// Ranges of one file in flight at once. A single connection measured ~100 Mb/s, so this is
    /// roughly what it takes for one large file to fill a gigabit line on its own.
    /// </summary>
    public int ConnectionsPerFile { get; init; } = 8;

    /// <summary>
    /// Chunks fetched or fetching beyond the one being read, which bounds the memory a download
    /// holds - <see cref="ChunkSize"/> times this. Wider than <see cref="ConnectionsPerFile"/>, so one
    /// slow chunk at the front does not idle every connection behind it straight away.
    /// </summary>
    public int WindowChunks { get; init; } = 12;

    /// <summary>Tries per chunk, the first included, before the download as a whole fails.</summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>A connection that delivers nothing for this long is abandoned and its range re-requested.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public SemaphoreSlim ConnectionBudget { get; init; } = _processConnectionBudget;
}


/// <inheritdoc cref="IModFileDownloader"/>
/// <remarks>
/// <para>
/// <b>A large file is fetched as parallel range GETs</b>, because one connection to blob storage
/// cannot fill a fast line. The first request asks for the first chunk only; a file that fits in
/// it costs exactly one request, and anything larger learns its size from the answer and fans out.
/// </para>
/// <para>
/// <b>What comes back is still one stream in file order.</b> The store hashes as it reads, and
/// keeping that the only path bytes take into it is worth the memory the reassembly window costs.
/// See docs/07-mod-sync-design.md#downloading.
/// </para>
/// </remarks>
public sealed class HttpModFileDownloader : IModFileDownloader
{
    private readonly HttpClient _httpClient;
    private readonly RangedDownloadOptions _options;


    public HttpModFileDownloader(HttpClient httpClient)
        : this(httpClient, RangedDownloadOptions.Default)
    {
    }

    internal HttpModFileDownloader(HttpClient httpClient, RangedDownloadOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }


    public async Task<ModFileDownload> OpenAsync(string link, IProgress<long>? bytesReceived, CancellationToken cancellationToken)
    {
        var response = await SendAsync(link, new RangeHeaderValue(0, _options.ChunkSize - 1), cancellationToken);

        try
        {
            if (response.StatusCode is HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // What storage says to any range of an empty blob. Nothing to split up.
                response.Dispose();
                response = await SendAsync(link, null, cancellationToken);
            }

            await EnsureSuccessAsync(response, cancellationToken);

            if (response.StatusCode is HttpStatusCode.PartialContent &&
                response.Content.Headers.ContentRange is { From: 0, To: long to, Length: long total } &&
                to + 1 < total)
            {
                var stream = new RangedDownloadStream(
                    _httpClient, link, response, (int)(to + 1), total, _options, bytesReceived, cancellationToken);

                return new ModFileDownload(stream, total, stream);
            }

            // All of it in the one answer: a file no bigger than a chunk, or a server that ignored
            // the range and sent a 200.
            var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var length = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength;

            return new ModFileDownload(content, length, response);
        }
        catch (Exception)
        {
            response.Dispose();
            throw;
        }
    }

    private Task<HttpResponseMessage> SendAsync(string link, RangeHeaderValue? range, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, link);
        request.Headers.Range = range;

        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Storage answers with an XML document naming the real cause - an expired SAS, a permission
        // the link was never granted - and none of that survives EnsureSuccessStatusCode.
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new HttpRequestException(
            $"Failed to download the mod file: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}",
            null,
            response.StatusCode);
    }
}


/// <summary>
/// One blob read as consecutive ranges over several connections, and handed out in file order.
/// </summary>
/// <remarks>
/// A queue of chunk fetches, oldest first: reading drains the front, and every chunk consumed lets
/// another start at the back. Each chunk retries on its own, so a dropped connection costs a range
/// rather than the file.
/// </remarks>
internal sealed class RangedDownloadStream : Stream
{
    private readonly HttpClient _httpClient;
    private readonly string _link;
    private readonly EntityTagHeaderValue? _entityTag;
    private readonly long _length;
    private readonly RangedDownloadOptions _options;
    private readonly SemaphoreSlim _connections;
    private readonly CancellationTokenSource _lifetime;
    private readonly Queue<Task<Chunk>> _pending = new();
    private readonly ReceivedBytes _received;

    private long _scheduled;
    private long _position;
    private Chunk? _current;
    private int _currentOffset;
    private bool _disposed;


    /// <param name="first">The answer to the opening request, whose body is the first chunk.</param>
    public RangedDownloadStream(
        HttpClient httpClient,
        string link,
        HttpResponseMessage first,
        int firstLength,
        long length,
        RangedDownloadOptions options,
        IProgress<long>? bytesReceived,
        CancellationToken cancellationToken)
    {
        _httpClient = httpClient;
        _link = link;
        _length = length;
        _options = options;
        _received = new ReceivedBytes(bytesReceived, length);

        // Pinned to the version the first answer came from, so a blob replaced mid-download fails
        // rather than splicing two files together. The hash would catch that too, but this says why.
        _entityTag = first.Headers.ETag;

        _connections = new SemaphoreSlim(options.ConnectionsPerFile);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _pending.Enqueue(ReadFirstAsync(first, firstLength, _lifetime.Token));
        _scheduled = firstLength;

        Schedule();
    }


    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }


    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_current is null)
        {
            if (_pending.Count == 0)
            {
                return 0;
            }

            // Peeked rather than dequeued, so a read cancelled while waiting leaves the chunk where
            // the next read - or Dispose - will find it.
            _current = await _pending.Peek().WaitAsync(cancellationToken);
            _pending.Dequeue();
            _currentOffset = 0;

            Schedule();
        }

        var chunk = _current.Value;
        var count = Math.Min(buffer.Length, chunk.Length - _currentOffset);

        chunk.Buffer.AsSpan(_currentOffset, count).CopyTo(buffer.Span);
        _currentOffset += count;
        _position += count;

        if (_currentOffset == chunk.Length)
        {
            ArrayPool<byte>.Shared.Return(chunk.Buffer);
            _current = null;
        }

        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && _disposed is false)
        {
            _disposed = true;
            _lifetime.Cancel();

            if (_current is Chunk current)
            {
                ArrayPool<byte>.Shared.Return(current.Buffer);
                _current = null;
            }

            // Whatever is still in flight finishes - or, far more likely, cancels - on its own time.
            // Its buffer goes back to the pool when it does, and its failure is observed rather than
            // left to surface as an unobserved task exception.
            var pending = _pending.ToArray();
            _pending.Clear();

            foreach (var task in pending)
            {
                _ = task.ContinueWith(
                    static x =>
                    {
                        if (x.IsCompletedSuccessfully)
                        {
                            ArrayPool<byte>.Shared.Return(x.Result.Buffer);
                        }

                        return x.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            var lifetime = _lifetime;

            _ = Task.WhenAll(pending).ContinueWith(
                _ => lifetime.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        base.Dispose(disposing);
    }


    /// <summary>Keeps the window full: starts chunks until it is, or until the file is covered.</summary>
    private void Schedule()
    {
        while (_pending.Count < _options.WindowChunks && _scheduled < _length)
        {
            var length = (int)Math.Min(_options.ChunkSize, _length - _scheduled);

            _pending.Enqueue(FetchAsync(_scheduled, length, _lifetime.Token));
            _scheduled += length;
        }
    }

    /// <summary>
    /// The opening request's body. Read outside the connection budget, since it was already open
    /// before there was anything to budget - and re-requested like any other range if it drops.
    /// </summary>
    private async Task<Chunk> ReadFirstAsync(HttpResponseMessage response, int length, CancellationToken cancellationToken)
    {
        using (response)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                return await ReadBodyAsync(response, length, stall, cancellationToken);
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken))
            {
                // Falls through to a fresh request for the same range.
            }
        }

        return await FetchAsync(0, length, cancellationToken, attempt: 2);
    }

    private async Task<Chunk> FetchAsync(long offset, int length, CancellationToken cancellationToken, int attempt = 1)
    {
        for (; ; attempt++)
        {
            await _connections.WaitAsync(cancellationToken);

            try
            {
                await _options.ConnectionBudget.WaitAsync(cancellationToken);

                try
                {
                    return await FetchOnceAsync(offset, length, cancellationToken);
                }
                finally
                {
                    _options.ConnectionBudget.Release();
                }
            }
            catch (Exception exception) when (attempt < _options.MaxAttempts && IsTransient(exception, cancellationToken))
            {
                // Retried below, after the connection has been handed back for somebody else.
            }
            finally
            {
                _connections.Release();
            }

            await Task.Delay(_options.RetryDelay * attempt, cancellationToken);
        }
    }

    private async Task<Chunk> FetchOnceAsync(long offset, int length, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _link);
        request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);

        if (_entityTag is not null)
        {
            request.Headers.IfMatch.Add(_entityTag);
        }

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(_options.StallTimeout);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);

        await HttpModFileDownloader.EnsureSuccessAsync(response, cancellationToken);

        if (response.StatusCode is not HttpStatusCode.PartialContent ||
            response.Content.Headers.ContentRange is not { From: long from, To: long to } ||
            from != offset ||
            to - from + 1 != length)
        {
            // Not a transient kind of wrong: the same request would get the same answer. Carrying a
            // success status is what keeps IsTransient from retrying it.
            throw new HttpRequestException(
                $"Storage answered a request for bytes {offset}-{offset + length - 1} with {(int)response.StatusCode} {response.Content.Headers.ContentRange}.",
                null,
                response.StatusCode);
        }

        return await ReadBodyAsync(response, length, stall, cancellationToken);
    }

    private async Task<Chunk> ReadBodyAsync(
        HttpResponseMessage response,
        int length,
        CancellationTokenSource stall,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        var filled = 0;

        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

            while (filled < length)
            {
                stall.CancelAfter(_options.StallTimeout);

                var read = await body.ReadAsync(buffer.AsMemory(filled, length - filled), stall.Token);

                if (read == 0)
                {
                    throw new IOException($"The connection closed {length - filled} bytes short of the end of the range.");
                }

                filled += read;
                _received.Add(read);
            }

            return new Chunk(buffer, length);
        }
        catch (Exception)
        {
            // Whatever this attempt got is thrown away with it, and the retry counts it again.
            _received.Add(-filled);

            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    /// Whether asking again could get a different answer. A stall and a dropped connection could;
    /// an expired link or a blob that changed underneath could not.
    /// </summary>
    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception switch
        {
            // Not the caller's token, so a stall or the client's own timeout.
            OperationCanceledException => true,
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: HttpStatusCode status } =>
                (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
            IOException => true,
            _ => false
        };
    }


    /// <param name="Buffer">Rented, and longer than <paramref name="Length"/>; returned once read.</param>
    private readonly record struct Chunk(byte[] Buffer, int Length);

    /// <summary>
    /// Bytes landed across every chunk in flight - what makes a progress bar move smoothly, where
    /// the reader's own count waits on the chunk at the front and then leaps several at once.
    /// </summary>
    /// <remarks>
    /// Reported a megabyte at a time rather than per socket read, which is the store's own cadence,
    /// and never backwards: a retried chunk takes its partial bytes off the count, and the bar holds
    /// still until the retry has made them up again.
    /// </remarks>
    private sealed class ReceivedBytes(IProgress<long>? progress, long length)
    {
        private const long _step = 1024 * 1024;

        private readonly Lock _gate = new();

        private long _received;
        private long _reported;


        public void Add(long bytes)
        {
            if (progress is null || bytes == 0)
            {
                return;
            }

            lock (_gate)
            {
                _received += bytes;

                if (_received >= _reported + _step || (_received == length && _received > _reported))
                {
                    _reported = _received;
                    progress.Report(_received);
                }
            }
        }
    }
}
