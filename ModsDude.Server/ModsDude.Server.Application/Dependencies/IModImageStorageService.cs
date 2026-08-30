using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Application.Dependencies;

public interface IModImageStorageService
{
    /// <summary>
    /// Which of <paramref name="hashes"/> are already stored. A batch because after the first import
    /// into a repo almost every image is already present, and asking one at a time turns a 2,000-mod
    /// import into tens of thousands of round trips before a single byte is uploaded.
    /// </summary>
    Task<IReadOnlyCollection<string>> CheckWhichExist(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken);

    Task Upload(string hash, string contentType, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// The stored bytes, or <c>null</c> when nothing is stored at that address. The caller owns the
    /// returned stream.
    /// </summary>
    Task<StoredModImage?> Download(string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Every stored image, for the reclamation sweep. See
    /// <see cref="IModStorageService.ListStoredMods"/>.
    /// </summary>
    IAsyncEnumerable<StoredBlob> ListStoredImages(CancellationToken cancellationToken);

    /// <summary>
    /// See <see cref="IModStorageService.DeleteStoredBlob"/>.
    /// </summary>
    Task DeleteStoredBlob(string blobName, CancellationToken cancellationToken);
}


public record StoredModImage(Stream Content, string ContentType);
