using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Application.Dependencies;

/// <summary>What the reclamation sweep does to a blob container, whichever kind of blob it holds.</summary>
public interface IReclaimableContainer
{
    /// <summary>
    /// Deletes by the exact name storage reported, which is what the sweep must use: re-deriving a
    /// path from a parsed address would put a parsing bug between deciding what is garbage and
    /// deleting it. And only while the blob is still the one listed - see <see cref="StoredBlob.Version"/>.
    /// </summary>
    /// <returns>Whether it was deleted: false where it had gone, or had been written since the listing.</returns>
    Task<bool> DeleteStoredBlob(StoredBlob blob, CancellationToken cancellationToken);

    /// <summary>The database id this container is marked with, or null where it is not marked - see <see cref="StorageBinding"/>.</summary>
    Task<Guid?> ReadBindingMarker(CancellationToken cancellationToken);

    /// <summary>Marks the container as belonging to <paramref name="bindingId"/>'s database, where it is not marked yet.</summary>
    Task WriteBindingMarker(Guid bindingId, CancellationToken cancellationToken);
}
