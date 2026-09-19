using System.Net;
using System.Net.Http.Headers;

namespace ModsDude.Client.Core.Transfers;

/// <summary>
/// A response body read under a <see cref="TransferRateLimiter"/>: every read pays for its bytes
/// before the next one starts.
/// </summary>
/// <remarks>
/// Reads are cut to <see cref="Slice"/>, because the caller's buffer is a megabyte and a megabyte
/// paid in one go under a slow limit is several seconds of nothing followed by a burst.
/// </remarks>
internal sealed class ThrottledReadStream(Stream inner, TransferRateLimiter limiter) : Stream
{
    /// <summary>The most a read or write moves before it pays for it.</summary>
    public const int Slice = 64 * 1024;


    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }


    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, Slice)], cancellationToken);

        await limiter.ConsumeAsync(read, cancellationToken);

        return read;
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
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}


/// <summary>
/// A request body sent under a <see cref="TransferRateLimiter"/>, a <see cref="ThrottledReadStream.Slice"/>
/// at a time.
/// </summary>
internal sealed class ThrottledContent : HttpContent
{
    private readonly ReadOnlyMemory<byte> _content;
    private readonly TransferRateLimiter _limiter;
    private readonly Action<long>? _sent;


    /// <param name="sent">Told how much of the body has gone so far, after every slice.</param>
    public ThrottledContent(ReadOnlyMemory<byte> content, TransferRateLimiter limiter, Action<long>? sent = null)
    {
        _content = content;
        _limiter = limiter;
        _sent = sent;

        Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    }


    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < _content.Length; offset += ThrottledReadStream.Slice)
        {
            var slice = _content.Slice(offset, Math.Min(ThrottledReadStream.Slice, _content.Length - offset));

            await stream.WriteAsync(slice, cancellationToken);
            await _limiter.ConsumeAsync(slice.Length, cancellationToken);

            _sent?.Invoke(offset + slice.Length);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _content.Length;

        return true;
    }
}
