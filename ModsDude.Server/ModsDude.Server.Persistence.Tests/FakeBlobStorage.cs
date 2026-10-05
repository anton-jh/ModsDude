using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using System.Runtime.CompilerServices;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The three blob containers as listings only. Anything else a storage service does is not needed by
/// the code under test and throws.
/// </summary>
public class FakeBlobStorage : IModStorageService, ISavegameStorageService, IModImageStorageService
{
    public List<StoredBlob> Mods { get; } = [];
    public List<StoredBlob> Savegames { get; } = [];
    public List<StoredBlob> Images { get; } = [];

    /// <summary>Runs before each blob is handed out, so a test can cancel or fail a listing midway.</summary>
    public Action<StoredBlob>? BeforeEachBlob { get; set; }


    public string ContentHashMetadataKey => "sha256";


    public IAsyncEnumerable<StoredBlob> ListStoredMods(CancellationToken cancellationToken) => ListAsync(Mods, cancellationToken);
    public IAsyncEnumerable<StoredBlob> ListStoredSavegames(CancellationToken cancellationToken) => ListAsync(Savegames, cancellationToken);
    public IAsyncEnumerable<StoredBlob> ListStoredImages(CancellationToken cancellationToken) => ListAsync(Images, cancellationToken);


    public Task EnsureContainerExists(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> CheckIfModExists(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<long?> GetModSize(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<string?> GetRecordedContentHash(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<string> GetUploadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<string> GetDownloadLink(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task DeleteMod(RepoId repoId, ModId modId, ModVersionId versionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> DeleteStoredBlob(StoredBlob blob, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> CheckIfSavegameExists(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<long?> GetSavegameSize(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> TryReuseSavegame(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<string?> GetRecordedContentHash(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<string> GetUploadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<string> GetDownloadLink(RepoId repoId, SavegameId savegameId, string contentHash, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyCollection<string>> CheckWhichExist(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task Upload(string hash, string contentType, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<StoredModImage?> Download(string hash, CancellationToken cancellationToken) => throw new NotSupportedException();


    private async IAsyncEnumerable<StoredBlob> ListAsync(List<StoredBlob> blobs, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var blob in blobs)
        {
            await Task.Yield();
            BeforeEachBlob?.Invoke(blob);
            cancellationToken.ThrowIfCancellationRequested();

            yield return blob;
        }
    }
}
