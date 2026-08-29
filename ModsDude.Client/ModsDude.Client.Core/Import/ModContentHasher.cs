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

    public static string Format(ReadOnlySpan<byte> digest) => Convert.ToHexStringLower(digest);

    /// <summary>
    /// Whether two recorded hashes describe the same bytes. Case-insensitive, because the hex casing
    /// is a formatting choice of whoever wrote the value and not part of the hash.
    /// </summary>
    public static bool Matches(string? left, string? right)
        => left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
