namespace ModsDude.Client.Core.Import;

/// <summary>
/// Storage already holds a blob at the link's address, so this upload committed nothing. Upload links
/// cannot overwrite, so whatever is there was committed by somebody else first.
/// </summary>
public sealed class BlobAlreadyExistsException(string message) : Exception(message);
