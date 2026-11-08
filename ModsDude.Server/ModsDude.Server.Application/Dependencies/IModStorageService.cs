using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Application.Dependencies;
public interface IModStorageService : IReclaimableContainer
{
    /// <summary>
    /// The blob metadata entry the uploading client writes the file's SHA-256 into. The API never
    /// sees the bytes, so the upload is the only moment the hash can be recorded against the blob.
    /// </summary>
    string ContentHashMetadataKey { get; }

    /// <inheritdoc cref="IModImageStorageService.EnsureContainerExists"/>
    Task EnsureContainerExists(CancellationToken cancellationToken);

    /// <summary>
    /// The stored file, or <c>null</c> when there is no such blob. Read from storage rather than from
    /// anything a client says, because the API never sees the bytes.
    /// </summary>
    Task<StoredModFile?> GetStoredMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="GetStoredMod"/>, and a write to the blob that changes what storage calls its state,
    /// so a reclamation sweep that listed it before this call cannot delete it. Called by whatever
    /// is about to make the blob referenced.
    /// </summary>
    Task<StoredModFile?> ClaimStoredMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
    Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
    Task DeleteMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Every mod blob there is, streamed rather than returned whole so that the reclamation sweep
    /// does not have to hold a container listing in memory before it can start.
    /// </summary>
    IAsyncEnumerable<StoredBlob> ListStoredMods(CancellationToken cancellationToken);
}


/// <param name="RecordedContentHash">
/// The SHA-256 the uploader recorded against the blob, or <c>null</c> where it carries no such record.
/// Not Azure's own content hash, which is MD5.
/// </param>
public record StoredModFile(long SizeBytes, string? RecordedContentHash);
