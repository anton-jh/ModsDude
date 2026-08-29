using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ModsDude.Client.Core.Import;

/// <summary>
/// Uploads a mod file to Azure blob storage over the SAS the server minted, staging it in blocks and
/// stamping the content hash into blob metadata as it commits.
/// </summary>
/// <remarks>
/// <para>
/// Staged blocks are what makes the never-register-before-upload invariant survive a torn upload: a
/// block blob only becomes visible when its block list is committed, so a run that dies partway
/// leaves nothing behind for <c>CheckIfModExists</c> to lie about. Nothing here resumes an
/// interrupted upload, deliberately - a resume scheme that committed partial content would give
/// that guarantee away.
/// </para>
/// <para>
/// The hash is accumulated over the same buffer the blocks are cut from, so a several-hundred-
/// megabyte archive is read once. It can only be known after the last block, which is the other
/// reason for committing separately: the metadata header carrying it goes on the commit, not on the
/// content.
/// </para>
/// <para>
/// Written against the REST API rather than <c>Azure.Storage.Blobs</c> because this is the only
/// place the client talks to storage, and the SDK is a large dependency for two requests.
/// </para>
/// </remarks>
public sealed class BlockBlobModFileUploader(HttpClient httpClient) : IModFileUploader
{
    /// <summary>
    /// Well under the 4000 MiB per-block ceiling, and small enough that a failed block is a cheap
    /// thing to lose. The buffer is held for the length of one upload, so a handful of concurrent
    /// mods costs a handful of these.
    /// </summary>
    private const int _blockSize = 4 * 1024 * 1024;

    /// <summary>
    /// Sent explicitly rather than left to the service to infer from the SAS, so the request does
    /// not silently change shape when the server's SAS builder is upgraded.
    /// </summary>
    private const string _storageApiVersion = "2021-08-06";


    public async Task<string> UploadAsync(ModFileUpload upload, CancellationToken cancellationToken)
    {
        using var content = upload.OpenContent();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[_blockSize];
        var blockIds = new List<string>();
        long transferred = 0;

        while (true)
        {
            var read = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);

            if (read == 0)
            {
                break;
            }

            digest.AppendData(buffer, 0, read);

            var blockId = MakeBlockId(blockIds.Count);
            blockIds.Add(blockId);

            await PutBlockAsync(upload.Link, blockId, buffer.AsMemory(0, read), cancellationToken);

            transferred += read;
            upload.BytesTransferred?.Report(transferred);

            if (read < buffer.Length)
            {
                break;
            }
        }

        var hash = ModContentHasher.Format(digest.GetHashAndReset());

        await CommitAsync(upload.Link, blockIds, upload.ContentHashMetadataKey, hash, cancellationToken);

        return hash;
    }


    private async Task PutBlockAsync(string link, string blockId, ReadOnlyMemory<byte> block, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Append(link, $"comp=block&blockid={Uri.EscapeDataString(blockId)}"))
        {
            Content = new ReadOnlyMemoryContent(block)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        await SendAsync(request, "stage a block of", cancellationToken);
    }

    private async Task CommitAsync(
        string link,
        IReadOnlyList<string> blockIds,
        string contentHashMetadataKey,
        string hash,
        CancellationToken cancellationToken)
    {
        var body = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>");

        foreach (var blockId in blockIds)
        {
            body.Append("<Latest>").Append(blockId).Append("</Latest>");
        }

        body.Append("</BlockList>");

        using var request = new HttpRequestMessage(HttpMethod.Put, Append(link, "comp=blocklist"))
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "application/xml")
        };

        request.Headers.TryAddWithoutValidation($"x-ms-meta-{contentHashMetadataKey}", hash);

        await SendAsync(request, "commit", cancellationToken);
    }

    private async Task SendAsync(HttpRequestMessage request, string what, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("x-ms-version", _storageApiVersion);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Storage answers with an XML error document naming the real cause - an expired SAS, a
        // permission the link was not granted - and none of that survives EnsureSuccessStatusCode.
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new HttpRequestException(
            $"Failed to {what} the mod file: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}",
            null,
            response.StatusCode);
    }


    /// <summary>Block ids must be equal-length and base64, and are only meaningful within one upload.</summary>
    private static string MakeBlockId(int index)
        => Convert.ToBase64String(Encoding.ASCII.GetBytes($"{index:D8}"));

    private static Uri Append(string link, string query)
        => new(link + (link.Contains('?') ? '&' : '?') + query);
}
