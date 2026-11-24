using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Storage.Services;

/// <summary>
/// Packed savegames, in their own container and addressed by the SHA-256 of their own bytes within
/// the savegame that owns them. See <see cref="ISavegameStorageService"/> for why the address is the
/// content here and the identity in <see cref="ModStorageService"/>.
/// </summary>
internal class SavegameStorageService(
    BlobServiceClient blobServiceClient,
    IBlobContainers blobContainers,
    IUserDelegationKeyCache keyCache,
    ITimeService timeService)
    : ISavegameStorageService
{
    /// <summary>
    /// Sent as <c>x-ms-meta-sha256</c>. The client writes it as it commits, because the API never
    /// sees the bytes and so cannot compute it.
    /// </summary>
    private const string _contentHashMetadataKey = "sha256";


    public string ContentHashMetadataKey => _contentHashMetadataKey;


    public async Task EnsureContainerExists(CancellationToken cancellationToken)
    {
        await blobServiceClient
            .GetBlobContainerClient(StorageContainerNames.Of(StorageContainer.Savegames))
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<long?> GetSavegameSize(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        var stored = await blobContainers.GetAsync(StorageContainer.Savegames, BlobNames.Savegame(repoId, savegameId, contentHash), cancellationToken);

        return stored?.Length;
    }

    public Task<string> GetUploadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        // Put Block needs Write. The link could overwrite an existing blob; the client's commit sends
        // If-None-Match: * so that it never does.
        return GetSasLink(repoId, savegameId, contentHash, BlobSasPermissions.Create | BlobSasPermissions.Write, cancellationToken);
    }

    public Task<string> GetDownloadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken)
    {
        return GetSasLink(repoId, savegameId, contentHash, BlobSasPermissions.Read, cancellationToken);
    }

    public async Task<bool> ClaimSavegame(RepoId repoId, SavegameId savegameId, string contentHash, SavegameBlobLabel? label, CancellationToken cancellationToken)
    {
        return await blobContainers.ClaimAsync(StorageContainer.Savegames, BlobNames.Savegame(repoId, savegameId, contentHash), label, cancellationToken) is not null;
    }


    private Task<string> GetSasLink(RepoId repoId, SavegameId savegameId, string contentHash, BlobSasPermissions permissions, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(repoId, savegameId, contentHash);

        return BlobSasLinks.CreateAsync(keyCache, blobClient, permissions, new DateTimeOffset(timeService.Now()), cancellationToken);
    }

    private BlobClient GetBlobClient(RepoId repoId, SavegameId savegameId, string contentHash)
    {
        return blobServiceClient
            .GetBlobContainerClient(StorageContainerNames.Of(StorageContainer.Savegames))
            .GetBlobClient(BlobNames.Savegame(repoId, savegameId, contentHash));
    }
}
