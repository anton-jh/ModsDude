namespace ModsDude.Server.Domain.Storage;

/// <summary>A blob as storage reports it.</summary>
/// <param name="Version">
/// What storage calls this state of the blob (its ETag). Deletes are conditional on it, so a blob written
/// or touched after it was read is kept.
/// </param>
/// <param name="Metadata">The blob's metadata as stored, keys compared without regard to case.</param>
public sealed record StoredBlob(
    string Name,
    DateTimeOffset LastModified,
    long Length,
    string? Version,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>
    /// The metadata entry the uploading client writes the file's SHA-256 into as it commits, sent as
    /// <c>x-ms-meta-sha256</c>. The API never sees the bytes, so it cannot compute it, and Azure's own
    /// content hash is MD5.
    /// </summary>
    public const string ContentHashKey = "sha256";


    /// <summary>The SHA-256 the uploader recorded, or null where the blob carries none.</summary>
    public string? RecordedContentHash => Metadata.GetValueOrDefault(ContentHashKey);
}
