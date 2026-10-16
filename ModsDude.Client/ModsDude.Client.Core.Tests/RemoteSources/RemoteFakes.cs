using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.RemoteSources;
using ModsDude.Client.Core.Tests.Sync;

namespace ModsDude.Client.Core.Tests.RemoteSources;

internal sealed class FakeRemoteModSource(string key = "hub", string displayName = "Hub") : IRemoteModSource
{
    public string Key => key;
    public string DisplayName => displayName;

    public IReadOnlyList<RemoteModVersion> Versions { get; set; } = [];

    public Func<Uri, bool> Allows { get; set; } = _ => true;


    public Task<RemoteModLookup> LookUpAsync(IReadOnlyCollection<ModKey> mods, CancellationToken cancellationToken)
        => Task.FromResult(new RemoteModLookup(DateTimeOffset.UnixEpoch, [.. Versions.Where(x => mods.Contains(x.ModId))]));

    public bool IsAllowedDownload(Uri uri) => Allows(uri);


    public static RemoteModVersion Version(string modId, string version, string? fileName = null)
        => new(
            ModKey.From(modId),
            ModVersionKey.From(version),
            modId,
            "Somebody",
            fileName ?? $"{modId}.zip",
            new Uri($"https://cdn.example.com/{modId}/{version}/{fileName ?? $"{modId}.zip"}"));
}


internal sealed class InMemoryRemoteDownloadNotes : IRemoteDownloadNotes
{
    private readonly Lock _lock = new();
    private readonly List<RemoteDownloadNote> _notes = [];


    public IReadOnlyList<RemoteDownloadNote> Notes
    {
        get
        {
            lock (_lock)
            {
                return [.. _notes];
            }
        }
    }


    public RemoteDownloadNote? Find(Uri url)
    {
        lock (_lock)
        {
            return _notes.LastOrDefault(x => x.Url == url);
        }
    }

    public void Record(RemoteDownloadNote note)
    {
        lock (_lock)
        {
            _notes.Add(note);
        }
    }
}


/// <summary>
/// Writes what a test says each link holds into a folder per download, as the real one does. Files are
/// read back by <see cref="Tests.GameAdapters.FakeMultiTargetBaseModAdapter"/>, which takes a zip's name as the
/// mod and its contents as the version.
/// </summary>
internal sealed class FakeRemoteModDownloader(TempDirectory root) : IRemoteModDownloader
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Uri, Func<string, Task>> _links = [];
    private int _inFlight;


    public int Runs { get; private set; }
    public int RunsDisposed { get; private set; }
    public int PeakConcurrency { get; private set; }
    public List<string> Discarded { get; } = [];

    /// <summary>Runs inside every download, where a test can hold it or cancel.</summary>
    public Func<Task>? OnDownload { get; set; }


    /// <summary>The link holds a zip named <paramref name="fileName"/> whose contents are the version it reads as.</summary>
    public void Serve(RemoteModVersion version, string heldVersion, string? fileName = null)
    {
        _links[version.DownloadUrl] = folder => File.WriteAllTextAsync(Path.Combine(folder, fileName ?? version.FileName), heldVersion);
    }

    /// <summary>The link holds nothing that reads as a mod.</summary>
    public void ServeNothing(RemoteModVersion version)
    {
        _links[version.DownloadUrl] = folder => File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "<html/>");
    }

    public void Fail(RemoteModVersion version, Exception exception)
    {
        _links[version.DownloadUrl] = _ => Task.FromException(exception);
    }

    public IRemoteDownloadRun BeginRun()
    {
        lock (_lock)
        {
            Runs++;
        }

        return new Run(this, root.CreateSubdirectory($"run-{Guid.NewGuid():N}"));
    }

    public void Sweep()
    {
    }


    private async Task<string> DownloadAsync(string runFolder, RemoteOffer offer, IProgress<RemoteDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _inFlight++;
            PeakConcurrency = Math.Max(PeakConcurrency, _inFlight);
        }

        try
        {
            if (OnDownload is not null)
            {
                await OnDownload();
            }

            await Task.Delay(5, cancellationToken);

            var folder = Path.Combine(runFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            await _links[offer.Version.DownloadUrl](folder);

            progress?.Report(new RemoteDownloadProgress(1, 1));

            return folder;
        }
        finally
        {
            lock (_lock)
            {
                _inFlight--;
            }
        }
    }


    private sealed class Run(FakeRemoteModDownloader owner, string folder) : IRemoteDownloadRun
    {
        public Task<string> DownloadAsync(RemoteOffer offer, IProgress<RemoteDownloadProgress>? progress, CancellationToken cancellationToken)
            => owner.DownloadAsync(folder, offer, progress, cancellationToken);

        public void Discard(string downloadFolder)
        {
            lock (owner._lock)
            {
                owner.Discarded.Add(downloadFolder);
            }

            Directory.Delete(downloadFolder, recursive: true);
        }

        public void Dispose()
        {
            lock (owner._lock)
            {
                owner.RunsDisposed++;
            }

            Directory.Delete(folder, recursive: true);
        }
    }
}
