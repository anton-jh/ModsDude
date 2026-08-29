namespace ModsDude.Client.Core.Import;

/// <summary>
/// Sends one mod file to the storage link the server minted, and reports the SHA-256 of what it
/// sent.
/// </summary>
/// <remarks>
/// The hash comes back from the upload rather than being computed beforehand because a mod archive
/// is hundreds of megabytes and the bytes should be read once, not twice.
/// </remarks>
public interface IModFileUploader
{
    /// <returns>The lowercase-hex SHA-256 of the uploaded bytes, to register the version against.</returns>
    Task<string> UploadAsync(ModFileUpload upload, CancellationToken cancellationToken);
}

/// <param name="Link">The SAS url from <c>CreateModUploadLink</c>.</param>
/// <param name="ContentHashMetadataKey">
/// The blob metadata entry to write the hash into, named by the server rather than agreed by
/// convention. The server reads it back to answer a later <c>FileAlreadyPresent</c>, so writing it
/// under the wrong key is not detected until some future import fails with nothing to explain it.
/// </param>
/// <param name="BytesTransferred">Reports progress across the whole file, for a per-mod row.</param>
public sealed record ModFileUpload(
    string Link,
    string ContentHashMetadataKey,
    Func<Stream> OpenContent)
{
    public IProgress<long>? BytesTransferred { get; init; }
}
