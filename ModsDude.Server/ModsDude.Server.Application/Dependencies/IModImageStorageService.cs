using ModsDude.Server.Domain.Mods;

namespace ModsDude.Server.Application.Dependencies;

public interface IModImageStorageService : IReclaimableContainer
{
    /// <summary>
    /// Creates the image container if it is not there. Called once at startup, because every other
    /// method here fails against a container that does not exist - and the client absorbs those
    /// failures by design, so a fresh storage account would otherwise present as imagery that
    /// silently never appears rather than as anything anybody could act on.
    /// </summary>
    Task EnsureContainerExists(CancellationToken cancellationToken);

    /// <summary>
    /// Writes to each stored image so a reclamation sweep that listed it before this call cannot delete
    /// it, and returns the hashes that are not stored. Called by whatever is about to make the images
    /// referenced.
    /// </summary>
    Task<IReadOnlyCollection<string>> TryReuse(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken);

    /// <summary>What every stored image is served as. Clients produce WebP only, and uploads are checked for it.</summary>
    string ContentType { get; }

    Task Upload(string hash, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// The stored bytes, or <c>null</c> when nothing is stored at that address. The caller owns the
    /// returned stream.
    /// </summary>
    Task<Stream?> Download(string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Every stored image, for the reclamation sweep. See
    /// <see cref="IModStorageService.ListStoredMods"/>.
    /// </summary>
    IAsyncEnumerable<StoredBlob> ListStoredImages(CancellationToken cancellationToken);
}
