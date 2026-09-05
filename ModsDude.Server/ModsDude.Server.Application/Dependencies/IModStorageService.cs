using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Application.Dependencies;
public interface IModStorageService
{
    /// <summary>
    /// The blob metadata entry the uploading client writes the file's SHA-256 into. The API never
    /// sees the bytes, so the upload is the only moment the hash can be recorded against the blob.
    /// </summary>
    string ContentHashMetadataKey { get; }

    /// <inheritdoc cref="IModImageStorageService.EnsureContainerExists"/>
    Task EnsureContainerExists(CancellationToken cancellationToken);

    Task<bool> CheckIfModExists(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// The SHA-256 the uploader recorded against the blob, or <c>null</c> when the blob is absent or
    /// carries no such record. Not Azure's own content hash, which is MD5.
    /// </summary>
    Task<string?> GetRecordedContentHash(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
    Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
    Task DeleteMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Every mod blob there is, streamed rather than returned whole so that the reclamation sweep
    /// does not have to hold a container listing in memory before it can start.
    /// </summary>
    IAsyncEnumerable<StoredBlob> ListStoredMods(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes by the exact name storage reported, which is what the sweep must use: re-deriving a
    /// path from a parsed address would put a parsing bug between deciding what is garbage and
    /// deleting it.
    /// </summary>
    Task DeleteStoredBlob(string blobName, CancellationToken cancellationToken);
}
