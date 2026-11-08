namespace ModsDude.Client.Core.Import;

/// <summary>
/// The bytes read during the upload did not hash to <see cref="ModFileUpload.ExpectedHash"/>, so the
/// upload committed nothing. The file changed between being hashed and being sent.
/// </summary>
public sealed class UploadedContentChangedException(string expectedHash, string actualHash)
    : Exception($"Expected the upload to hash to '{expectedHash}' but it hashed to '{actualHash}'. Nothing was committed.")
{
    public string ExpectedHash { get; } = expectedHash;
    public string ActualHash { get; } = actualHash;
}
