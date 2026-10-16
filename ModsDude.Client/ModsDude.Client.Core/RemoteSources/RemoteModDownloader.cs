using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Disks;
using ModsDude.Client.Core.Helpers;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;
using ModsDude.Client.Core.Transfers;
using System.Net;
using System.Net.Http.Headers;

namespace ModsDude.Client.Core.RemoteSources;

/// <summary>
/// Downloads through <see cref="HttpModFileDownloader"/>, from a client that follows no redirect by
/// itself: every hop is checked against the source before anything is fetched from it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One connection per file.</b> The downloader splits a large file over several connections for
/// blob storage; a third party's CDN gets one per file, and the import bounds how many files run.
/// </para>
/// <para>
/// <b>Written beside its final name and moved into place</b>, so a file that is there is complete. A
/// run owns a folder of its own under <see cref="Root"/>; disposing it, and every sweep, deletes what
/// no live run owns.
/// </para>
/// </remarks>
public sealed class RemoteModDownloader : IRemoteModDownloader
{
    public const string HttpClientName = "RemoteMods";

    /// <summary>Larger than any mod anybody would download.</summary>
    public const long MaxBytes = 8L * 1024 * 1024 * 1024;

    private const int _maxRedirects = 3;
    private const int _maxProbeAttempts = 3;
    private const string _partialExtension = ".part";

    private static readonly byte[] _zipSignature = [0x50, 0x4B, 0x03, 0x04];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TransferLimits _limits;
    private readonly IDiskSpaceGuard _diskSpace;
    private readonly ILogger _log;
    private readonly RangedDownloadOptions _options;
    private readonly TimeSpan _retryDelay;

    private readonly Lock _lock = new();
    private readonly HashSet<string> _liveRuns = new(StringComparer.OrdinalIgnoreCase);


    public RemoteModDownloader(
        IHttpClientFactory httpClientFactory,
        TransferLimits limits,
        IDiskSpaceGuard diskSpace,
        ILogger<RemoteModDownloader> logger)
        : this(
            httpClientFactory,
            limits,
            diskSpace,
            logger,
            Path.Combine(FileSystemHelper.GetAppDataDirectory(), "remote-downloads"),
            TimeSpan.FromSeconds(1))
    {
    }

    /// <param name="root">Where runs put their folders. Named so tests can point it somewhere else.</param>
    internal RemoteModDownloader(
        IHttpClientFactory httpClientFactory,
        TransferLimits limits,
        IDiskSpaceGuard diskSpace,
        ILogger logger,
        string root,
        TimeSpan retryDelay)
    {
        _httpClientFactory = httpClientFactory;
        _limits = limits;
        _diskSpace = diskSpace;
        _log = logger;
        _retryDelay = retryDelay;

        Root = root;

        _options = RangedDownloadOptions.Default with
        {
            ConnectionsPerFile = 1,
            WindowChunks = 2,
            RetryDelay = retryDelay
        };
    }


    public string Root { get; }


    public IRemoteDownloadRun BeginRun()
    {
        Sweep();

        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N"));

        lock (_lock)
        {
            _liveRuns.Add(folder);
        }

        Directory.CreateDirectory(folder);

        return new Run(this, folder);
    }

    public void Sweep()
    {
        IReadOnlyList<string> present;

        try
        {
            if (Directory.Exists(Root) is false)
            {
                return;
            }

            present = [.. Directory.EnumerateFileSystemEntries(Root)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(exception, "Could not list the remote download folder {Folder}.", Root);

            return;
        }

        foreach (var entry in present)
        {
            lock (_lock)
            {
                if (_liveRuns.Contains(entry))
                {
                    continue;
                }
            }

            if (Directory.Exists(entry))
            {
                FileSystemHelper.TryDeleteDirectory(entry, _log);
            }
            else
            {
                FileSystemHelper.TryDeleteFile(entry, _log);
            }
        }
    }


    private void End(string folder)
    {
        FileSystemHelper.TryDeleteDirectory(folder, _log);

        lock (_lock)
        {
            _liveRuns.Remove(folder);
        }
    }

    private async Task<string> DownloadAsync(
        string runFolder,
        RemoteOffer offer,
        IProgress<RemoteDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fileName = offer.Version.FileName;

        if (IsUsableFileName(fileName) is false)
        {
            throw RemoteDownloadException.BadFileName(fileName);
        }

        var folder = Path.Combine(runFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, fileName);
        var partial = path + _partialExtension;

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);

            var (url, length) = await ProbeAsync(client, offer, cancellationToken);

            await FetchAsync(client, url, length, partial, progress, cancellationToken);

            File.Move(partial, path);

            return folder;
        }
        catch (Exception)
        {
            FileSystemHelper.TryDeleteDirectory(folder, _log);

            throw;
        }
    }

    /// <summary>
    /// Follows the offer's link to where the bytes are, checking every hop, and learns how big the file
    /// is. Asks for one byte, so a link that answers with a page instead is refused before a download.
    /// </summary>
    private async Task<(Uri Url, long? Length)> ProbeAsync(HttpClient client, RemoteOffer offer, CancellationToken cancellationToken)
    {
        var url = offer.Version.DownloadUrl;

        for (var hop = 0; ; hop++)
        {
            if (offer.Source.IsAllowedDownload(url) is false)
            {
                throw RemoteDownloadException.NotAllowed(url);
            }

            using var response = await SendProbeAsync(client, url, cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (hop >= _maxRedirects)
                {
                    throw RemoteDownloadException.TooManyRedirects();
                }

                url = response.Headers.Location is Uri location
                    ? new Uri(url, location)
                    : throw RemoteDownloadException.NotAnArchive($"A redirect from {url.Host} named no location.");

                continue;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                throw RemoteDownloadException.Gone(offer.SourceName);
            }

            if (response.StatusCode is HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                throw RemoteDownloadException.NotAnArchive($"{url.Host} answered an empty file.");
            }

            await HttpModFileDownloader.EnsureSuccessAsync(response, cancellationToken);

            if (response.Content.Headers.ContentType?.MediaType is string mediaType
                && mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                throw RemoteDownloadException.NotAnArchive($"{url.Host} answered with {mediaType}.");
            }

            var length = response.StatusCode is HttpStatusCode.PartialContent
                ? response.Content.Headers.ContentRange?.Length
                : response.Content.Headers.ContentLength;

            return (url, length);
        }
    }

    private async Task<HttpResponseMessage> SendProbeAsync(HttpClient client, Uri url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);

            try
            {
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (IsTransient(response.StatusCode) is false || attempt == _maxProbeAttempts)
                {
                    return response;
                }

                _log.LogInformation("{Host} answered {Status}; trying again.", url.Host, (int)response.StatusCode);
                response.Dispose();
            }
            catch (Exception exception) when (attempt < _maxProbeAttempts && IsTransient(exception, cancellationToken))
            {
                _log.LogInformation(exception, "Reaching {Host} failed; trying again.", url.Host);
            }

            await Task.Delay(_retryDelay * attempt, cancellationToken);
        }
    }

    private async Task FetchAsync(
        HttpClient client,
        Uri url,
        long? probedLength,
        string partial,
        IProgress<RemoteDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (probedLength > MaxBytes)
        {
            throw RemoteDownloadException.TooLarge(MaxBytes);
        }

        if (probedLength is long expected)
        {
            _diskSpace.EnsureRoomFor(DiskSpaceNeeds.For(partial, expected));
        }

        var downloader = new HttpModFileDownloader(client, _options, _limits.Download);

        using var download = await downloader.OpenAsync(url.AbsoluteUri, null, cancellationToken);

        var total = download.Length ?? probedLength;

        if (total > MaxBytes)
        {
            throw RemoteDownloadException.TooLarge(MaxBytes);
        }

        var buffer = new byte[FileSystemHelper.StreamBufferSize];
        var head = new List<byte>(_zipSignature.Length);
        var received = 0L;

        await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, FileSystemHelper.StreamBufferSize, FileOptions.Asynchronous))
        {
            int read;

            while ((read = await download.Content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (head.Count < _zipSignature.Length)
                {
                    head.AddRange(buffer.Take(Math.Min(read, _zipSignature.Length - head.Count)));

                    if (head.Count == _zipSignature.Length && head.SequenceEqual(_zipSignature) is false)
                    {
                        throw RemoteDownloadException.NotAnArchive("The file does not start like a zip archive.");
                    }
                }

                received += read;

                if (received > MaxBytes)
                {
                    throw RemoteDownloadException.TooLarge(MaxBytes);
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                progress?.Report(new RemoteDownloadProgress(received, total));
            }

            if (head.Count < _zipSignature.Length)
            {
                throw RemoteDownloadException.NotAnArchive("The file is too short to be an archive.");
            }

            if (total is long length && received != length)
            {
                throw new IOException($"The download ended at {received} of {length} bytes.");
            }

            file.Flush(flushToDisk: true);
        }
    }


    private static bool IsUsableFileName(string fileName)
        => string.IsNullOrWhiteSpace(fileName) is false
        && fileName == Path.GetFileName(fileName)
        && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && fileName.IndexOfAny(['/', '\\', ':']) < 0
        && fileName.Trim('.', ' ').Length > 0
        && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedirect(HttpStatusCode status)
        => status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException or IOException
        || (exception is OperationCanceledException && cancellationToken.IsCancellationRequested is false);


    private sealed class Run(RemoteModDownloader owner, string folder) : IRemoteDownloadRun
    {
        public Task<string> DownloadAsync(RemoteOffer offer, IProgress<RemoteDownloadProgress>? progress, CancellationToken cancellationToken)
            => owner.DownloadAsync(folder, offer, progress, cancellationToken);

        public void Discard(string downloadFolder) => FileSystemHelper.TryDeleteDirectory(downloadFolder, owner._log);

        public void Dispose() => owner.End(folder);
    }
}
