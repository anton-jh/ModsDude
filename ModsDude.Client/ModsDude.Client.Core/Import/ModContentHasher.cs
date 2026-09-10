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
