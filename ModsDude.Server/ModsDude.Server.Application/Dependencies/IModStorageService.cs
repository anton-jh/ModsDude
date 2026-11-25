using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;

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

    /// <summary>
    /// The stored file, or <c>null</c> when there is no such blob. Read from storage rather than from
    /// anything a client says, because the API never sees the bytes.
    /// </summary>
    Task<StoredModFile?> GetStoredMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// A link the client uploads over, to the upload's own blob. Registering copies it to the version's
    /// address. The same <paramref name="uploadId"/> names the same blob, so a renewed link carries on
    /// where the expired one stopped.
    /// </summary>
    Task<string> GetUploadLink(RepoId repoId, UserId userId, UploadId uploadId, CancellationToken cancellationToken);

    Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken);
}


/// <param name="RecordedContentHash">
/// The SHA-256 the uploader recorded against the blob, or <c>null</c> where it carries no such record.
/// Not Azure's own content hash, which is MD5.
/// </param>
public record StoredModFile(long SizeBytes, string? RecordedContentHash);
