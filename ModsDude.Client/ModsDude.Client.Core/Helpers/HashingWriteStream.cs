using ModsDude.Client.Core.Import;
using System.Security.Cryptography;

namespace ModsDude.Client.Core.Helpers;

/// <summary>
/// A write-only stream that hashes everything on its way through to <paramref name="inner"/>.
/// </summary>
/// <remarks>
/// <b>Never disposes what it wraps</b>, so the caller keeps control of the stream it is writing to,
/// and <b>never claims to seek</b>: a writer that could seek back would patch bytes the hash has
/// already seen.
/// </remarks>
internal sealed class HashingWriteStream(Stream inner) : Stream
{
    private readonly IncrementalHash _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _written;


    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }


    /// <summary>The SHA-256 of everything written so far, lowercase hex.</summary>
    public string GetHash() => ModContentHasher.Format(_digest.GetCurrentHash());

    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _digest.AppendData(buffer);
        inner.Write(buffer);
        _written += buffer.Length;
    }

    public override void WriteByte(byte value)
        => Write([value]);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _digest.AppendData(buffer.Span);
        await inner.WriteAsync(buffer, cancellationToken);
        _written += buffer.Length;
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _digest.Dispose();
        }
    }
}
