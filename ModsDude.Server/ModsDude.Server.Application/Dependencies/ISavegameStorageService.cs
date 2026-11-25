using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Application.Dependencies;

/// <summary>
/// Packed savegames, stored at <c>{repoId}/{savegameId}/{contentHash}</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bytes are addressed by content, not by identity.</b> This is the one place savegame
/// storage deliberately diverges from <see cref="IModStorageService"/>, which addresses a file by
/// the mod version it belongs to. Numbering the blob instead would have two people checking in at
/// the same moment mint upload links for the same name, so whichever wrote second would silently
/// replace the other's bytes - and the stale-base check that decides who takes the head runs after
/// the writing, by which point the loser's save is already gone. Two concurrent check-ins that
/// hash differently write two different blobs and neither is lost.
/// </para>
/// <para>
/// The second consequence is restore: copying an old snapshot forward to the head is a pure metadata
/// operation, because the bytes are already stored under the address the new snapshot names. A
/// check-in of something already stored costs nothing at all, which is what makes a 400 MB save
/// cheap to keep re-checking in.
/// </para>
/// <para>
/// The repo and savegame still appear in the path even though the hash alone would identify the
/// bytes. Deduplication across savegames is worth much less than a blob naming the repo and savegame
/// it belongs to, which <see cref="BlobNames.TryParseSavegame"/> reads off the name alone.
/// </para>
/// </remarks>
public interface ISavegameStorageService
{
    /// <summary>
    /// The blob metadata entry the uploading client writes the packed save's SHA-256 into. The API
    /// never sees the bytes, so the upload is the only moment the hash can be recorded against the
    /// blob. Same key and same reason as <see cref="IModStorageService.ContentHashMetadataKey"/>.
    /// </summary>
    string ContentHashMetadataKey { get; }

    /// <summary>
    /// Creates the container if it is not there. Called at startup rather than before each write:
    /// a fresh storage account should not present as savegames that silently never upload.
    /// </summary>
    Task EnsureContainerExists(CancellationToken cancellationToken);

    /// <summary>
    /// A link the client uploads the packed save over, to the upload's own blob. Checking in copies it to
    /// the save's address. The same <paramref name="uploadId"/> names the same blob, so a renewed link
    /// carries on where the expired one stopped.
    /// </summary>
    Task<string> GetUploadLink(RepoId repoId, UserId userId, UploadId uploadId, CancellationToken cancellationToken);

    Task<string> GetDownloadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken);

    /// <summary>
    /// Whether these bytes are stored for this savegame, labelling the blob where it carries no label
    /// yet. The write gives the blob a new version, so a deletion run that read it before this call
    /// cannot delete it. Called by whatever is about to make the blob referenced, or to reuse it.
    /// </summary>
    /// <remarks>
    /// True is the answer that makes a re-check-in free: the address is the hash, so a blob that is
    /// there is the same save.
    /// </remarks>
    /// <param name="label">Null where the caller does not know the savegame well enough to describe it.</param>
    Task<bool> ClaimSavegame(RepoId repoId, SavegameId savegameId, string contentHash, SavegameBlobLabel? label, CancellationToken cancellationToken);
}
