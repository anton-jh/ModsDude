using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Disks;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Tests.Sync;
using ModsDude.Client.Core.Transfers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ModsDude.Client.Core.Tests.RemoteSources;

public class RemoteModDownloaderTests : IDisposable
{
    private static readonly byte[] _archive = [0x50, 0x4B, 0x03, 0x04, .. Encoding.UTF8.GetBytes("the rest of the archive")];

    private readonly TempDirectory _temp = new("remote-downloads");
    private readonly FakeCdn _cdn = new();
    private readonly FakeRemoteModSource _source = new() { Allows = x => x.Scheme == "https" && x.Host.EndsWith(".example.com") };
    private readonly RemoteModDownloader _downloader;


    public RemoteModDownloaderTests()
    {
        _downloader = new RemoteModDownloader(
            new FakeHttpClientFactory(_cdn),
            new TransferLimits(TimeProvider.System),
            new RoomyDisk(),
            NullLogger.Instance,
            _temp.Combine("downloads"),
            TimeSpan.Zero);
    }


    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }


    [Fact]
    public async Task The_archive_lands_alone_in_a_folder_of_its_own_under_the_sources_name()
    {
        var offer = Offer("FS25_Plough");
        _cdn.Serve(offer.Version.DownloadUrl, _archive);

        using var run = _downloader.BeginRun();

        var folder = await run.DownloadAsync(offer, null, CancellationToken.None);

        var file = Assert.Single(Directory.EnumerateFiles(folder));
        Assert.Equal("FS25_Plough.zip", Path.GetFileName(file));
        Assert.Equal(_archive, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task Progress_counts_up_to_the_size_of_the_file()
    {
        var offer = Offer("FS25_Plough");
        _cdn.Serve(offer.Version.DownloadUrl, _archive);

        var reports = new List<RemoteDownloadProgress>();

        using var run = _downloader.BeginRun();
        await run.DownloadAsync(offer, new Inline<RemoteDownloadProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(new RemoteDownloadProgress(_archive.Length, _archive.Length), reports[^1]);
    }

    [Fact]
    public async Task A_redirect_to_an_allowed_host_is_followed()
    {
        var offer = Offer("FS25_Plough");
        var target = new Uri("https://mirror.example.com/FS25_Plough.zip");

        _cdn.Redirect(offer.Version.DownloadUrl, target);
        _cdn.Serve(target, _archive);

        using var run = _downloader.BeginRun();

        var folder = await run.DownloadAsync(offer, null, CancellationToken.None);

        Assert.Equal(_archive, await File.ReadAllBytesAsync(Path.Combine(folder, "FS25_Plough.zip")));
    }

    [Fact]
    public async Task A_redirect_to_a_host_the_source_does_not_allow_is_never_followed()
    {
        var offer = Offer("FS25_Plough");
        var target = new Uri("https://evil.test/FS25_Plough.zip");

        _cdn.Redirect(offer.Version.DownloadUrl, target);
        _cdn.Serve(target, _archive);

        using var run = _downloader.BeginRun();

        var exception = await Assert.ThrowsAsync<RemoteDownloadException>(() => run.DownloadAsync(offer, null, CancellationToken.None));

        Assert.Equal("The download points somewhere that is not allowed.", exception.UserMessage);
        Assert.DoesNotContain(_cdn.Requests, x => x.Host == "evil.test");
    }

    [Fact]
    public async Task A_link_that_is_not_allowed_is_never_requested()
    {
        var offer = Offer("FS25_Plough") with { };
        var refused = offer with { Version = offer.Version with { DownloadUrl = new Uri("http://cdn.example.com/FS25_Plough.zip") } };

        using var run = _downloader.BeginRun();

        await Assert.ThrowsAsync<RemoteDownloadException>(() => run.DownloadAsync(refused, null, CancellationToken.None));

        Assert.Empty(_cdn.Requests);
    }

    [Fact]
    public async Task Redirects_without_end_are_given_up_on()
    {
        var offer = Offer("FS25_Plough");
        _cdn.Redirect(offer.Version.DownloadUrl, offer.Version.DownloadUrl);

        using var run = _downloader.BeginRun();

        var exception = await Assert.ThrowsAsync<RemoteDownloadException>(() => run.DownloadAsync(offer, null, CancellationToken.None));

        Assert.Equal("The download kept redirecting.", exception.UserMessage);
    }

    [Fact]
    public async Task Every_request_carries_the_sources_referrer_redirect_hops_included()
    {
        _source.DownloadReferrer = new Uri("https://hub.example.com/");

        var offer = Offer("FS25_Plough");
        var target = new Uri("https://mirror.example.com/FS25_Plough.zip");

        _cdn.Redirect(offer.Version.DownloadUrl, target);
        _cdn.Serve(target, _archive);

        using var run = _downloader.BeginRun();
        await run.DownloadAsync(offer, null, CancellationToken.None);

        Assert.Equal(3, _cdn.Referrers.Count);
        Assert.All(_cdn.Referrers, x => Assert.Equal(_source.DownloadReferrer, x));
    }

    [Fact]
    public async Task A_source_without_a_referrer_sends_none()
    {
        var offer = Offer("FS25_Plough");
        _cdn.Serve(offer.Version.DownloadUrl, _archive);

        using var run = _downloader.BeginRun();
        await run.DownloadAsync(offer, null, CancellationToken.None);

        Assert.NotEmpty(_cdn.Referrers);
        Assert.All(_cdn.Referrers, Assert.Null);
    }

    [Fact]
    public async Task A_page_answered_instead_of_the_archive_is_refused()
    {
        var offer = Offer("FS25_Plough");
        _cdn.Serve(offer.Version.DownloadUrl, Encoding.UTF8.GetBytes("<html>Log in</html>"), "text/html");

        using var run = _downloader.BeginRun();

        var exception = await Assert.ThrowsAsync<RemoteDownloadException>(() => run.DownloadAsync(offer, null, CancellationToken.None));

        Assert.Equal("The download is not a mod archive.", exception.UserMessage);
    }

    [Fact]
    public async Task A_file_that_does_not_start_like_a_zip_is_refused_and_nothing_is_left()
    {
        var offer = Offer("FS25_Plough");
        _cdn.Serve(offer.Version.DownloadUrl, Encoding.UTF8.GetBytes("not an archive at all"));

        using var run = _downloader.BeginRun();

        await Assert.ThrowsAsync<RemoteDownloadException>(() => run.DownloadAsync(offer, null, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(_temp.Combine("downloads"), "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task A_link_that_is_gone_says_so(HttpStatusCode status)
    {
        var offer = Offer("FS25_Plough");
        _cdn.Answer(offer.Version.DownloadUrl, _ => new HttpResponseMessage(status));

        using var run = _downloader.BeginRun();

        var exception = await Assert.ThrowsAsync<RemoteDownloadException>(() => run.DownloadAsync(offer, null, CancellationToken.None));

        Assert.Equal("Hub no longer has this file.", exception.UserMessage);
    }

    [Fact]
    public async Task A_file_bigger_than_the_limit_is_refused_before_it_is_downloaded()
    {
        var offer = Offer("FS25_Plough");
        _cdn.Answer(offer.Version.DownloadUrl, _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([0x50]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, RemoteModDownloader.MaxBytes + 1);
            return response;
        });

        using var run = _downloader.BeginRun();

        await Assert.ThrowsAsync<RemoteDownloadException>(() => run.DownloadAsync(offer, null, CancellationToken.None));

        Assert.Single(_cdn.Requests);
    }

    [Fact]
    public async Task A_busy_server_is_asked_again()
    {
        var offer = Offer("FS25_Plough");
        var asked = 0;

        _cdn.Answer(offer.Version.DownloadUrl, request => Interlocked.Increment(ref asked) == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : FakeCdn.File(request, _archive, "application/zip"));

        using var run = _downloader.BeginRun();

        var folder = await run.DownloadAsync(offer, null, CancellationToken.None);

        Assert.Single(Directory.EnumerateFiles(folder));
    }

    [Theory]
    [InlineData("../FS25_Plough.zip")]
    [InlineData("FS25_Plough.exe")]
    [InlineData("")]
    public async Task A_file_name_that_could_land_anywhere_else_is_refused(string fileName)
    {
        var offer = Offer("FS25_Plough");
        var renamed = offer with { Version = offer.Version with { FileName = fileName } };

        using var run = _downloader.BeginRun();

        await Assert.ThrowsAsync<RemoteDownloadException>(() => run.DownloadAsync(renamed, null, CancellationToken.None));

        Assert.Empty(_cdn.Requests);
    }

    [Fact]
    public async Task Cancelling_leaves_nothing_behind()
    {
        using var cancellation = new CancellationTokenSource();

        var offer = Offer("FS25_Plough");
        _cdn.Answer(offer.Version.DownloadUrl, request =>
        {
            cancellation.Cancel();
            return FakeCdn.File(request, _archive, "application/zip");
        });

        using var run = _downloader.BeginRun();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.DownloadAsync(offer, null, cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(_temp.Combine("downloads"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Ending_a_run_deletes_everything_it_downloaded()
    {
        var offer = Offer("FS25_Plough");
        _cdn.Serve(offer.Version.DownloadUrl, _archive);

        var run = _downloader.BeginRun();
        await run.DownloadAsync(offer, null, CancellationToken.None);

        run.Dispose();

        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Combine("downloads")));
    }

    [Fact]
    public async Task A_sweep_removes_what_no_live_run_owns_and_nothing_a_live_one_does()
    {
        var leftover = _temp.WriteFile(Path.Combine("downloads", "crashed-run", "a", "FS25_Old.zip"), "left behind");

        var offer = Offer("FS25_Plough");
        _cdn.Serve(offer.Version.DownloadUrl, _archive);

        using var run = _downloader.BeginRun();
        var folder = await run.DownloadAsync(offer, null, CancellationToken.None);

        _downloader.Sweep();

        Assert.False(File.Exists(leftover));
        Assert.Single(Directory.EnumerateFiles(folder));
    }


    private RemoteOffer Offer(string modId) => new(_source, FakeRemoteModSource.Version(modId, "1.0"));


    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class RoomyDisk : IDiskSpaceGuard
    {
        public void EnsureRoomFor(DiskSpaceNeeds needs)
        {
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Answers per link, following no redirect by itself - as the real client is configured.</summary>
    private sealed class FakeCdn : HttpMessageHandler
    {
        private readonly Dictionary<Uri, Func<HttpRequestMessage, HttpResponseMessage>> _answers = [];
        private readonly Lock _lock = new();
        private readonly List<Uri> _requests = [];
        private readonly List<Uri?> _referrers = [];


        public IReadOnlyList<Uri> Requests
        {
            get
            {
                lock (_lock)
                {
                    return [.. _requests];
                }
            }
        }

        public IReadOnlyList<Uri?> Referrers
        {
            get
            {
                lock (_lock)
                {
                    return [.. _referrers];
                }
            }
        }


        public void Answer(Uri url, Func<HttpRequestMessage, HttpResponseMessage> answer) => _answers[url] = answer;

        public void Serve(Uri url, byte[] content, string contentType = "application/zip")
            => Answer(url, request => File(request, content, contentType));

        public void Redirect(Uri url, Uri location)
            => Answer(url, _ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = location } });

        public static HttpResponseMessage File(HttpRequestMessage request, byte[] content, string contentType)
        {
            HttpResponseMessage response;

            if (request.Headers.Range?.Ranges.SingleOrDefault() is { From: long from } range)
            {
                var to = Math.Min(range.To ?? content.Length - 1, content.Length - 1);

                response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(content[(int)from..((int)to + 1)])
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, content.Length);
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
            }

            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            return response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _requests.Add(request.RequestUri!);
                _referrers.Add(request.Headers.Referrer);
            }

            return Task.FromResult(_answers.TryGetValue(request.RequestUri!, out var answer)
                ? answer(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
