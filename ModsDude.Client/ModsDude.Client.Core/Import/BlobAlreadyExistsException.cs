namespace ModsDude.Client.Core.Import;

/// <summary>
/// The file an upload was sending is stored at its address already, so the upload stopped with nothing
/// left to send.
/// </summary>
public sealed class BlobAlreadyExistsException(string message) : Exception(message);
