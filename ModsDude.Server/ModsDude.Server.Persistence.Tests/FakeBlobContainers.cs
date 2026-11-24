using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Storage;
using System.Runtime.CompilerServices;

namespace ModsDude.Server.Persistence.Tests;

/// <summary>
/// The three containers in memory, with versions that move on every write the way ETags do, so the
/// conditional writes the code under test relies on behave as they do against storage.
/// </summary>
public class FakeBlobContainers : IBlobContainers
{
    private readonly Dictionary<StorageContainer, SortedDictionary<string, StoredBlob>> _blobs =
        StorageContainerNames.All.ToDictionary(x => x, _ => new SortedDictionary<string, StoredBlob>(StringComparer.Ordinal));

    private readonly Dictionary<StorageContainer, (string Content, string Version)> _markers = [];

    private int _versions;


    public string StorageAccount { get; set; } = "test.blob.core.windows.net";

    public int? SoftDeleteDays { get; set; } = 31;

    /// <summary>What a write stamps as the blob's last modified time.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

    /// <summary>Runs before each blob is handed out by a listing, so a test can cancel or fail it midway.</summary>
    public Action<StoredBlob>? BeforeEachBlob { get; set; }

    /// <summary>Runs after each read of one blob, so a test can change things between the read and what follows it.</summary>
    public Func<StorageContainer, string, Task>? AfterGet { get; set; }

    /// <summary>Runs before each delete, so a test can change the blob under it.</summary>
    public Action<StorageContainer, string>? BeforeDelete { get; set; }

    /// <summary>Whether a marker write is refused as if somebody else had written it first.</summary>
    public Func<StorageContainer, bool> RefuseMarkerWrite { get; set; } = _ => false;

    /// <summary>Whether a delete that goes through answers as if the blob had already gone.</summary>
    public bool LoseDeleteAnswers { get; set; }

    public List<(StorageContainer Container, string Name)> Deleted { get; } = [];


    public StoredBlob Put(StorageContainer container, string name, long length = 1, DateTimeOffset? lastModified = null, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var blob = new StoredBlob(
            name,
            lastModified ?? Now,
            length,
            NextVersion(),
            new Dictionary<string, string>(metadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));

        _blobs[container][name] = blob;

        return blob;
    }

    /// <summary>A write that changes nothing but the version and the time, like a claim.</summary>
    public void Touch(StorageContainer container, string name)
    {
        _blobs[container][name] = _blobs[container][name] with { Version = NextVersion(), LastModified = Now };
    }

    public void Remove(StorageContainer container, string name) => _blobs[container].Remove(name);

    public StoredBlob? Find(StorageContainer container, string name) => _blobs[container].GetValueOrDefault(name);

    public void SetMarkerText(StorageContainer container, string content) => _markers[container] = (content, NextVersion());

    public ContainerMarker? MarkerOf(StorageContainer container) =>
        _markers.TryGetValue(container, out var marker) ? ContainerMarker.Parse(marker.Content) : null;


    public async IAsyncEnumerable<StoredBlob> ListAsync(StorageContainer container, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var blob in _blobs[container].Values.ToList())
        {
            await Task.Yield();
            BeforeEachBlob?.Invoke(blob);
            cancellationToken.ThrowIfCancellationRequested();

            yield return blob;
        }
    }

    public Task<bool> IsEmptyAsync(StorageContainer container, CancellationToken cancellationToken) => Task.FromResult(_blobs[container].Count == 0);

    public async Task<StoredBlob?> GetAsync(StorageContainer container, string blobName, CancellationToken cancellationToken)
    {
        var blob = Find(container, blobName);

        if (AfterGet is not null)
        {
            await AfterGet(container, blobName);
        }

        return blob;
    }

    public Task<StoredBlob?> ClaimAsync(StorageContainer container, string blobName, BlobLabel? label, CancellationToken cancellationToken)
    {
        if (Find(container, blobName) is not { } current)
        {
            return Task.FromResult<StoredBlob?>(null);
        }

        _blobs[container][blobName] = current with
        {
            Version = NextVersion(),
            LastModified = Now,
            Metadata = new Dictionary<string, string>(label?.ApplyTo(current.Metadata) ?? current.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        return Task.FromResult<StoredBlob?>(current);
    }

    public Task<BlobDeleteResult> DeleteAsync(StorageContainer container, string blobName, string version, CancellationToken cancellationToken)
    {
        BeforeDelete?.Invoke(container, blobName);

        if (Find(container, blobName) is not { } current)
        {
            return Task.FromResult(BlobDeleteResult.Gone);
        }

        if (current.Version != version)
        {
            return Task.FromResult(BlobDeleteResult.Changed);
        }

        _blobs[container].Remove(blobName);
        Deleted.Add((container, blobName));

        // What a retried delete whose first answer was lost hears.
        return Task.FromResult(LoseDeleteAnswers ? BlobDeleteResult.Gone : BlobDeleteResult.Deleted);
    }

    public Task<bool> WriteLabelAsync(StorageContainer container, StoredBlob blob, BlobLabel label, CancellationToken cancellationToken)
    {
        if (Find(container, blob.Name) is not { } current || current.Version != blob.Version)
        {
            return Task.FromResult(false);
        }

        _blobs[container][blob.Name] = current with
        {
            Version = NextVersion(),
            LastModified = Now,
            Metadata = new Dictionary<string, string>(label.ApplyTo(current.Metadata), StringComparer.OrdinalIgnoreCase)
        };

        return Task.FromResult(true);
    }

    public Task<MarkerRead> ReadMarkerAsync(StorageContainer container, CancellationToken cancellationToken)
    {
        if (!_markers.TryGetValue(container, out var marker))
        {
            return Task.FromResult(new MarkerRead(null, null, false));
        }

        try
        {
            return Task.FromResult(new MarkerRead(ContainerMarker.Parse(marker.Content), marker.Version, false));
        }
        catch (FormatException)
        {
            return Task.FromResult(new MarkerRead(null, marker.Version, true));
        }
    }

    public Task<bool> WriteMarkerAsync(StorageContainer container, ContainerMarker marker, string? expectedVersion, CancellationToken cancellationToken)
    {
        var current = _markers.TryGetValue(container, out var existing) ? existing.Version : null;

        if (RefuseMarkerWrite(container) || current != expectedVersion)
        {
            return Task.FromResult(false);
        }

        _markers[container] = (marker.Format(), NextVersion());

        return Task.FromResult(true);
    }

    public Task<int?> GetSoftDeleteDaysAsync(CancellationToken cancellationToken) => Task.FromResult(SoftDeleteDays);


    private string NextVersion() => $"v{Interlocked.Increment(ref _versions)}";
}
