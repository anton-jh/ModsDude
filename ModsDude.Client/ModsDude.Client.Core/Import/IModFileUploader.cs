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
    /// <exception cref="BlobAlreadyExistsException">Something is already stored at the link's address.</exception>
    /// <exception cref="UploadedContentChangedException">The bytes did not hash to <see cref="ModFileUpload.ExpectedHash"/>.</exception>
    /// <exception cref="TimeoutException">A request stopped making progress, on every attempt.</exception>
    Task<string> UploadAsync(ModFileUpload upload, CancellationToken cancellationToken);
}

/// <param name="Link">The SAS url from <c>CreateModUploadLink</c>.</param>
/// <param name="ContentHashMetadataKey">
/// The blob metadata entry to write the hash into, named by the server rather than agreed by
/// convention. The server reads it back to answer a later <c>FileAlreadyPresent</c>, so writing it
/// under the wrong key is not detected until some future import fails with nothing to explain it.
/// </param>
/// <param name="BytesTransferred">Reports progress across the whole file, for a per-mod row.</param>
/// <param name="ExpectedHash">
/// What the bytes must hash to, where the caller hashed them already. A mismatch commits nothing,
/// which matters where the hash is the address: committed under the wrong one, the bytes could never
/// be replaced.
/// </param>
/// <param name="RenewLink">
/// Mints a fresh link to the same address, for an upload that outlives its link. Blocks already
/// staged stay staged, so the upload carries on where it was. Null fails the upload instead.
/// </param>
public sealed record ModFileUpload(
    string Link,
    string ContentHashMetadataKey,
    Func<Stream> OpenContent)
{
    public IProgress<long>? BytesTransferred { get; init; }
    public string? ExpectedHash { get; init; }
    public Func<CancellationToken, Task<string>>? RenewLink { get; init; }
}
