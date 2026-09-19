using System.Buffers;
using System.Security.Cryptography;

namespace ModsDude.Client.Core.Import;

/// <summary>
/// The one place the client turns mod bytes into the SHA-256 the repo records, so that everything
/// producing or checking a content hash agrees on the encoding.
/// </summary>
/// <remarks>
/// Lowercase hex, matching the server's <c>ContentHash</c>. Import computes it while uploading
/// rather than through here - one pass over the bytes, not two - and falls back to this only on the
/// path where there is nothing to upload but the hash still has to be established.
/// </remarks>
public static class ModContentHasher
{
    public static async Task<string> ComputeAsync(Stream content, CancellationToken cancellationToken)
    {
        using var algorithm = SHA256.Create();

        return Format(await algorithm.ComputeHashAsync(content, cancellationToken));
    }

    /// <param name="bytesRead">
    /// How far through the stream the hash has got, cumulatively, at most once per
    /// <see cref="ReportEvery"/> - a multi-hundred-megabyte archive is minutes of nothing else to
    /// look at. Null is exactly the overload above.
    /// </param>
    public static async Task<string> ComputeAsync(Stream content, IProgress<long>? bytesRead, CancellationToken cancellationToken)
    {
        if (bytesRead is null)
        {
            return await ComputeAsync(content, cancellationToken);
        }

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = ArrayPool<byte>.Shared.Rent(ReportEvery);

        try
        {
            long total = 0;
            int read;

            while ((read = await content.ReadAsync(buffer.AsMemory(0, ReportEvery), cancellationToken)) > 0)
            {
                digest.AppendData(buffer, 0, read);
                total += read;

                bytesRead.Report(total);
            }

            return Format(digest.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>One read per report, so the throttle is the buffer rather than a second piece of bookkeeping.</summary>
    private const int ReportEvery = 1024 * 1024;

    public static string Format(ReadOnlySpan<byte> digest) => Convert.ToHexStringLower(digest);

    /// <summary>
    /// Whether two recorded hashes describe the same bytes: ordinary equality on the one format, and
    /// null on either side is never a match.
    /// </summary>
    /// <remarks>
    /// <b>Not case-insensitive, deliberately.</b> This is the single place a hash string is minted and
    /// the server refuses any spelling but this one, so two parts of the application disagreeing over
    /// hex casing is a bug in whichever of them wrote the odd spelling - and absorbing it at every
    /// comparison site hides that bug rather than fixing it, while inviting the next comparison to
    /// lean on the same leniency. See docs/10-savegame-profile-binding.md#one-hash-format.
    /// </remarks>
    public static bool Matches(string? left, string? right)
        => left is not null && right is not null && string.Equals(left, right, StringComparison.Ordinal);
}
