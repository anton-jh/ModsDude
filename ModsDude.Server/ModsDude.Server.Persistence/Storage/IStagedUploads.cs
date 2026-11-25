using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// Makes sure a file is stored at its address before a row refers to it: claims the blob already there,
/// or copies the caller's upload into place. Called by registering a mod version, checking in and
/// publishing a savegame, before they commit.
/// </summary>
public interface IStagedUploads
{
    /// <param name="contentHash">What the registration says the file is. Refused unless the stored file records the same.</param>
    Task<StagedUploadResult> StoreModAsync(
        RepoId repoId, ModId modId, ModVersionId versionId, UserId userId, UploadId? uploadId,
        string contentHash, ModBlobLabel label, CancellationToken cancellationToken);

    /// <param name="sizeBytes">How long the client says its archive is. An upload of another length is refused.</param>
    Task<StagedUploadResult> StoreSavegameAsync(
        RepoId repoId, SavegameId savegameId, string contentHash, UserId userId, UploadId? uploadId,
        long sizeBytes, SavegameBlobLabel label, CancellationToken cancellationToken);
}


public enum StagedUploadOutcome
{
    /// <summary>The file is stored at its address, claimed or copied there just now.</summary>
    Stored,

    /// <summary>Nothing is at the address, and there is no upload to copy there.</summary>
    Missing,

    /// <summary>The stored file or the upload records another content hash than the one asked for.</summary>
    HashMismatch,

    /// <summary>The upload is not the file it should be: another length, or no valid content hash recorded.</summary>
    UploadInvalid,

    /// <summary>The upload changed or went while it was being copied.</summary>
    UploadChanged
}

/// <param name="Blob">The stored file where it was stored; for a hash mismatch, the file whose recorded hash differs.</param>
public sealed record StagedUploadResult(StagedUploadOutcome Outcome, StoredBlob? Blob);
