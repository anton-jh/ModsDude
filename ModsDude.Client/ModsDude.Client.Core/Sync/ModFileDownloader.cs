namespace ModsDude.Client.Core.Sync;

/// <summary>
/// Fetches one mod file from the storage link the server minted.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="Import.IModFileUploader"/>, and deliberately as thin: it hands back
/// a stream and knows nothing about hashes. Verification belongs to the store, which is the one
/// place bytes become an address - see <see cref="ContentStore.IngestAsync"/>.
/// </remarks>
public interface IModFileDownloader
{
    /// <param name="link">The read SAS from <c>CreateModDownloadLink</c>.</param>
    /// <returns>The blob's contents, and its length where storage declared one.</returns>
    Task<ModFileDownload> OpenAsync(string link, CancellationToken cancellationToken);
}

/// <summary>Disposing this releases the response the stream is reading from.</summary>
public sealed class ModFileDownload(Stream content, long? length, IDisposable response)
    : IDisposable
{
    public Stream Content { get; } = content;

    /// <summary>Null when storage did not say, which is what a chunked response looks like.</summary>
    public long? Length { get; } = length;


    public void Dispose()
    {
        Content.Dispose();
        response.Dispose();
    }
}


/// <inheritdoc cref="IModFileDownloader"/>
public sealed class HttpModFileDownloader(HttpClient httpClient) : IModFileDownloader
{
    public async Task<ModFileDownload> OpenAsync(string link, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        try
        {
            if (response.IsSuccessStatusCode is false)
            {
                // Storage answers with an XML document naming the real cause - an expired SAS, a
                // permission the link was never granted - and none of that survives
                // EnsureSuccessStatusCode.
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);

                throw new HttpRequestException(
                    $"Failed to download the mod file: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}",
                    null,
                    response.StatusCode);
            }

            var content = await response.Content.ReadAsStreamAsync(cancellationToken);

            return new ModFileDownload(content, response.Content.Headers.ContentLength, response);
        }
        catch (Exception)
        {
            response.Dispose();
            throw;
        }
    }
}
