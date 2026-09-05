using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

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
/// The second consequence is restore: copying an old version forward to the head is a pure metadata
/// operation, because the bytes are already stored under the address the new version names. A
/// check-in of something already stored costs nothing at all, which is what makes a 400 MB save
/// cheap to keep re-checking in.
/// </para>
/// <para>
/// The repo and savegame still appear in the path even though the hash alone would identify the
/// bytes. Deduplication across savegames is worth much less than a savegame's blobs being deletable
/// by knowing only the savegame - and the reclamation sweep can read an address off a name without
/// consulting anything, which is what
/// <see cref="BlobReclamation.TryParseSavegameBlobName"/> relies on.
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
    /// Whether these bytes are already stored for this savegame. True is the answer that makes a
    /// re-check-in free: the address is the hash, so a blob that is there is the same save.
    /// </summary>
    /// <summary>
    /// Creates the container if it is not there. Called at startup rather than before each write:
    /// a fresh storage account should not present as savegames that silently never upload.
    /// </summary>
    Task EnsureContainerExists(CancellationToken cancellationToken);

    Task<bool> CheckIfSavegameExists(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken);

    /// <summary>
    /// The SHA-256 the uploader recorded against the blob, or <c>null</c> when the blob is absent or
    /// carries no such record. Not Azure's own content hash, which is MD5.
    /// </summary>
    /// <remarks>
    /// For a savegame this <b>verifies</b> the address rather than discovering it, since the address
    /// already is the hash. An upload whose recorded hash disagrees with the address it was written
    /// to was not produced by a client that hashed what it actually sent, and the check-in it
    /// belongs to should be refused rather than recorded - a version whose <c>ContentHash</c> does
    /// not describe its bytes is a backup nobody can trust to be the save they checked in.
    /// </remarks>
    Task<string?> GetRecordedContentHash(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken);

    /// <summary>
    /// A link the client uploads the packed save over. The client names the hash before it uploads,
    /// because the hash is the address - which means it has to have hashed the file already, and the
    /// bytes it then sends can be checked against the name it asked for.
    /// </summary>
    Task<string> GetUploadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken);

    Task<string> GetDownloadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the bytes at one address.
    /// </summary>
    /// <remarks>
    /// <b>Not the same thing as deleting a version.</b> Several versions of a savegame can name one
    /// content hash - a restore does exactly that, and so does checking in a save that was played
    /// and reverted - so pruning one version must only reach here once no remaining version names
    /// its hash. The reclamation sweep is the safety net for the case that is missed, not the
    /// mechanism relied on.
    /// </remarks>
    Task DeleteSavegame(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken);

    /// <summary>
    /// Every savegame blob there is, streamed rather than returned whole so that the reclamation
    /// sweep does not have to hold a container listing in memory before it can start. See
    /// <see cref="IModStorageService.ListStoredMods"/>.
    /// </summary>
    IAsyncEnumerable<StoredBlob> ListStoredSavegames(CancellationToken cancellationToken);

    /// <summary>
    /// See <see cref="IModStorageService.DeleteStoredBlob"/>.
    /// </summary>
    Task DeleteStoredBlob(string blobName, CancellationToken cancellationToken);
}
