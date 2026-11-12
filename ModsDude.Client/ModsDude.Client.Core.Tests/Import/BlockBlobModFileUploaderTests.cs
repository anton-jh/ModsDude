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
        _uploader = Uploader(TransferLimits.From(new TransferLimitSettings(), TimeProvider.System));
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


    [Fact]
    public async Task A_block_that_stalls_is_sent_again()
    {
        var calls = 0;
        _storage.Handle = async (request, cancellationToken) =>
        {
            if (request.Query.Contains("comp=block&") && Interlocked.Increment(ref calls) == 1)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        };

        await _uploader.UploadAsync(Upload("content"), CancellationToken.None);

        Assert.Equal(2, _storage.Requests.Count(x => x.Query.Contains("comp=block&")));
        Assert.Contains(_storage.Requests, x => x.Query.Contains("comp=blocklist"));
    }

    [Fact]
    public async Task A_block_that_stalls_every_time_fails_as_a_timeout_and_commits_nothing()
    {
        _storage.Handle = async (request, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Created);
        };

        await Assert.ThrowsAsync<TimeoutException>(() => _uploader.UploadAsync(Upload("content"), CancellationToken.None));

        Assert.Equal(4, _storage.Requests.Count);
        Assert.DoesNotContain(_storage.Requests, x => x.Query.Contains("comp=blocklist"));
    }

    [Fact]
    public async Task Cancelling_a_stalled_block_is_not_mistaken_for_a_stall()
    {
        using var cancellation = new CancellationTokenSource();
        _storage.Handle = async (request, cancellationToken) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Created);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _uploader.UploadAsync(Upload("content"), cancellation.Token));

        Assert.Single(_storage.Requests);
    }

    [Fact]
    public async Task A_block_refused_for_a_passing_reason_is_sent_again()
    {
        var calls = 0;
        _storage.BlockResponse = () => Interlocked.Increment(ref calls) == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("<Error/>") }
            : new HttpResponseMessage(HttpStatusCode.Created);

        await _uploader.UploadAsync(Upload("content"), CancellationToken.None);

        Assert.Equal(2, _storage.Requests.Count(x => x.Query.Contains("comp=block&")));
    }

    [Fact]
    public async Task A_block_refused_for_good_is_not_sent_again()
    {
        _storage.BlockResponse = () => Refusal(HttpStatusCode.BadRequest, "InvalidBlockId");

        await Assert.ThrowsAsync<HttpRequestException>(() => _uploader.UploadAsync(Upload("content"), CancellationToken.None));

        Assert.Single(_storage.Requests);
    }

    [Fact]
    public async Task An_expired_link_is_renewed_and_the_upload_carries_on_with_the_new_one()
    {
        var calls = 0;
        _storage.BlockResponse = () => Interlocked.Increment(ref calls) == 1
            ? Refusal(HttpStatusCode.Forbidden, "AuthenticationFailed")
            : new HttpResponseMessage(HttpStatusCode.Created);

        var renewals = 0;
        var upload = Upload("content") with
        {
            RenewLink = _ =>
            {
                renewals++;
                return Task.FromResult("https://storage.example/blob?renewed");
            }
        };

        await _uploader.UploadAsync(upload, CancellationToken.None);

        Assert.Equal(1, renewals);
        Assert.Contains("?sas", _storage.Requests[0].Query);
        Assert.All(_storage.Requests.Skip(1), x => Assert.Contains("?renewed", x.Query));
        Assert.Contains(_storage.Requests, x => x.Query.Contains("comp=blocklist"));
    }

    [Fact]
    public async Task A_renewed_link_refused_again_fails_the_upload()
    {
        _storage.BlockResponse = () => Refusal(HttpStatusCode.Forbidden, "AuthenticationFailed");

        var renewals = 0;
        var upload = Upload("content") with
        {
            RenewLink = _ =>
            {
                renewals++;
                return Task.FromResult("https://storage.example/blob?renewed");
            }
        };

        await Assert.ThrowsAsync<HttpRequestException>(() => _uploader.UploadAsync(upload, CancellationToken.None));

        Assert.Equal(1, renewals);
        Assert.DoesNotContain(_storage.Requests, x => x.Query.Contains("comp=blocklist"));
    }

    [Fact]
    public async Task Under_a_limit_blocks_share_the_connections_it_allows()
    {
        // 128 KiB/s allows one connection.
        var uploader = Uploader(TransferLimits.From(new TransferLimitSettings { UploadBytesPerSecond = 128 * 1024 }, TimeProvider.System));
        var active = 0;
        var most = 0;

        _storage.Handle = async (request, cancellationToken) =>
        {
            // Blocks only: a commit carries a few hundred bytes and takes no connection from the limit.
            if (request.Query.Contains("comp=block&"))
            {
                InterlockedMax(ref most, Interlocked.Increment(ref active));

                await Task.Delay(50, cancellationToken);
                Interlocked.Decrement(ref active);
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        };

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => uploader.UploadAsync(Upload("content"), CancellationToken.None)));

        Assert.Equal(1, most);
    }


    private BlockBlobModFileUploader Uploader(TransferLimits limits)
        => new(new HttpClient(_storage), limits, TimeProvider.System)
        {
            StallTimeout = TimeSpan.FromMilliseconds(200),
            RetryDelay = TimeSpan.FromMilliseconds(1)
        };

    private static void InterlockedMax(ref int target, int value)
    {
        int current;

        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
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

        /// <summary>Answers every request instead of the two above, where a test sets it.</summary>
        public Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>>? Handle { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await request.Content!.ReadAsByteArrayAsync(cancellationToken);

            var query = request.RequestUri!.Query;
            var ifNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values) ? string.Join(",", values) : null;
            var recorded = new RecordedRequest(query, ifNoneMatch);

            lock (Requests)
            {
                Requests.Add(recorded);
            }

            if (Handle is not null)
            {
                return await Handle(recorded, cancellationToken);
            }

            return query.Contains("comp=blocklist") ? CommitResponse() : BlockResponse();
        }
    }
}
