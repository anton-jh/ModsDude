using ModsDude.Server.Domain.Storage;

namespace ModsDude.Server.Application.Exceptions;

/// <summary>A blob kept changing while it was being claimed, so the claim gave up. Asking again later can succeed.</summary>
public class BlobBusyException(StorageContainer container, string blobName, Exception innerException)
    : ApplicationException($"Blob {blobName} in container {StorageContainerNames.Of(container)} kept changing while it was being claimed.", innerException);
