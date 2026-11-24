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
    IReadOnlyDictionary<string, string> Metadata);
