using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Application.Dependencies;

/// <summary>
/// The three blob containers as plain blobs: claims, and for storage maintenance listing, the binding
/// markers, labels and conditional deletes. Every write here is conditional on a version read before it.
/// </summary>
public interface IBlobContainers
{
    /// <summary>The host of the storage account, which a binding records.</summary>
    string StorageAccount { get; }

    /// <summary>Every blob in the container with its metadata, apart from the binding marker.</summary>
    IAsyncEnumerable<StoredBlob> ListAsync(StorageContainer container, CancellationToken cancellationToken);

    /// <summary>Whether the container holds no blob apart from the binding marker.</summary>
    Task<bool> IsEmptyAsync(StorageContainer container, CancellationToken cancellationToken);

    /// <summary>The blob as stored now, or null where there is none.</summary>
    Task<StoredBlob?> GetAsync(StorageContainer container, string blobName, CancellationToken cancellationToken);

    /// <summary>
    /// Writes to the blob, adding <paramref name="label"/> where it carries none, so it gets a new version
    /// and a deletion run that read it before this call cannot delete it. Called by whatever is about to
    /// make the blob referenced, before that commits.
    /// </summary>
    /// <returns>The blob as read just before the write, or null where there is none.</returns>
    /// <exception cref="Exceptions.BlobBusyException">The blob kept changing between the read and the write.</exception>
    Task<StoredBlob?> ClaimAsync(StorageContainer container, string blobName, BlobLabel? label, CancellationToken cancellationToken);

    /// <summary>Deletes the blob while it is still at <paramref name="version"/>.</summary>
    Task<BlobDeleteResult> DeleteAsync(StorageContainer container, string blobName, string version, CancellationToken cancellationToken);

    /// <summary>Adds <paramref name="label"/> to the blob's metadata while it is still at <see cref="StoredBlob.Version"/>.</summary>
    /// <returns>Whether it was written: false where the blob had gone or changed, or storage refused the metadata.</returns>
    Task<bool> WriteLabelAsync(StorageContainer container, StoredBlob blob, BlobLabel label, CancellationToken cancellationToken);

    Task<MarkerRead> ReadMarkerAsync(StorageContainer container, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the marker while it is still at <paramref name="expectedVersion"/>, or, with null, only
    /// where there is none.
    /// </summary>
    /// <returns>Whether it was written: false where the marker had changed since it was read.</returns>
    Task<bool> WriteMarkerAsync(StorageContainer container, ContainerMarker marker, string? expectedVersion, CancellationToken cancellationToken);

    /// <summary>How many days the account keeps deleted blobs: 0 where soft delete is off, null where the setting can't be read.</summary>
    Task<int?> GetSoftDeleteDaysAsync(CancellationToken cancellationToken);
}


public enum BlobDeleteResult
{
    Deleted,

    /// <summary>There was no blob by that name.</summary>
    Gone,

    /// <summary>The blob is no longer at the version asked for, and stays.</summary>
    Changed
}

/// <param name="Marker">The marker, or null where there is none or it can't be read.</param>
/// <param name="Version">The marker blob's version, or null where there is no marker blob.</param>
/// <param name="IsUnreadable">Whether there is a marker blob that holds something other than a marker.</param>
public sealed record MarkerRead(ContainerMarker? Marker, string? Version, bool IsUnreadable);
