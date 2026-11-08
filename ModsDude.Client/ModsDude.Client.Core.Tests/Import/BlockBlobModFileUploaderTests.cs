using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Transfers;
using System.Net;
using System.Text;

namespace ModsDude.Client.Core.Tests.Import;

/// <summary>
/// Upload links cannot overwrite, and two uploads of one file can run at once. What this guards is
/// that neither can corrupt the other, and that a lost race is a refusal the caller recognises.
/// </summary>
public class BlockBlobModFileUploaderTests
{
    private const string _link = "https://storage.example/blob?sas";

    private readonly RecordingStorage _storage = new();
    private readonly BlockBlobModFileUploader _uploader;


    public BlockBlobModFileUploaderTests()
    {
        _uploader = new BlockBlobModFileUploader(new HttpClient(_storage), TransferLimits.From(new TransferLimitSettings(), TimeProvider.System));
    }


    [Fact]
    public async Task The_commit_refuses_to_replace_a_stored_blob()
    {
        await _uploader.UploadAsync(Upload("content"), CancellationToken.None);

        var commit = Assert.Single(_storage.Requests, x => x.Query.Contains("comp=blocklist"));
        Assert.Equal("*", commit.IfNoneMatch);
    }

    [Fact]
    public async Task Two_uploads_of_one_file_stage_different_block_ids()
    {
        await _uploader.UploadAsync(Upload("content"), CancellationToken.None);
        await _uploader.UploadAsync(Upload("content"), CancellationToken.None);

        var blockIds = _storage.Requests.Where(x => x.Query.Contains("comp=block&")).Select(x => x.Query).ToList();

        Assert.Equal(2, blockIds.Count);
        Assert.NotEqual(blockIds[0], blockIds[1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "BlobAlreadyExists")]
    [InlineData(HttpStatusCode.PreconditionFailed, "ConditionNotMet")]
    public async Task A_commit_that_finds_a_blob_already_stored_says_so(HttpStatusCode status, string errorCode)
    {
        _storage.CommitResponse = () => Refusal(status, errorCode);

        await Assert.ThrowsAsync<BlobAlreadyExistsException>(() => _uploader.UploadAsync(Upload("content"), CancellationToken.None));
    }

    [Theory]
    [InlineData("AuthenticationFailed")]
    [InlineData("AuthorizationPermissionMismatch")]
    public async Task A_refused_link_is_not_mistaken_for_a_stored_blob(string errorCode)
    {
        _storage.BlockResponse = () => Refusal(HttpStatusCode.Forbidden, errorCode);

        await Assert.ThrowsAsync<HttpRequestException>(() => _uploader.UploadAsync(Upload("content"), CancellationToken.None));
    }

    [Fact]
    public async Task Bytes_that_do_not_hash_to_the_expected_hash_are_never_committed()
    {
        var expected = ModContentHasher.Format(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("what was packed")));

        var exception = await Assert.ThrowsAsync<UploadedContentChangedException>(
            () => _uploader.UploadAsync(Upload("what was sent") with { ExpectedHash = expected }, CancellationToken.None));

        Assert.Equal(expected, exception.ExpectedHash);
        Assert.DoesNotContain(_storage.Requests, x => x.Query.Contains("comp=blocklist"));
    }

    [Fact]
    public async Task Bytes_that_hash_to_the_expected_hash_are_committed()
    {
        var expected = ModContentHasher.Format(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("content")));

        var hash = await _uploader.UploadAsync(Upload("content") with { ExpectedHash = expected }, CancellationToken.None);

        Assert.Equal(expected, hash);
        Assert.Contains(_storage.Requests, x => x.Query.Contains("comp=blocklist"));
    }


    private static ModFileUpload Upload(string content)
        => new(_link, "sha256", () => new MemoryStream(Encoding.UTF8.GetBytes(content)));

    private static HttpResponseMessage Refusal(HttpStatusCode status, string errorCode)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("<Error/>") };
        response.Headers.Add("x-ms-error-code", errorCode);

        return response;
    }


    private sealed record RecordedRequest(string Query, string? IfNoneMatch);

    private sealed class RecordingStorage : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];
        public Func<HttpResponseMessage> BlockResponse { get; set; } = () => new HttpResponseMessage(HttpStatusCode.Created);
        public Func<HttpResponseMessage> CommitResponse { get; set; } = () => new HttpResponseMessage(HttpStatusCode.Created);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await request.Content!.ReadAsByteArrayAsync(cancellationToken);

            var query = request.RequestUri!.Query;
            var ifNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values) ? string.Join(",", values) : null;

            Requests.Add(new RecordedRequest(query, ifNoneMatch));

            return query.Contains("comp=blocklist") ? CommitResponse() : BlockResponse();
        }
    }
}
