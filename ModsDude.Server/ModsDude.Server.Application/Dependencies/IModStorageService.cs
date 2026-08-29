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

    Task<bool> CheckIfModExists(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// The SHA-256 the uploader recorded against the blob, or <c>null</c> when the blob is absent or
    /// carries no such record. Not Azure's own content hash, which is MD5.
    /// </summary>
    Task<string?> GetRecordedContentHash(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
    Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
    Task DeleteMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
}
