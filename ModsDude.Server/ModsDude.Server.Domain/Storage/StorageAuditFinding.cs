using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Storage;

/// <summary>One thing the last audit found wrong between storage and the database.</summary>
public class StorageAuditFinding
{
    private StorageAuditFinding()
    {
    }


    public long Id { get; private set; }
    public StorageContainer Container { get; private set; }
    public string BlobName { get; private set; } = string.Empty;
    public StorageFindingKind Kind { get; private set; }

    /// <summary>The repo the name belongs to, where it parses and has one.</summary>
    public RepoId? RepoId { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Null for <see cref="StorageFindingKind.Missing"/>, which has no blob to describe.</summary>
    public DateTime? LastModified { get; private set; }

    /// <inheritdoc cref="StoredBlob.Version"/>
    public string? Version { get; private set; }

    public IReadOnlyDictionary<string, string> Metadata { get; private set; } = new Dictionary<string, string>();


    public static StorageAuditFinding Stored(StorageContainer container, StorageFindingKind kind, StoredBlob blob)
    {
        return new StorageAuditFinding
        {
            Container = container,
            BlobName = blob.Name,
            Kind = kind,
            RepoId = BlobNames.OwningRepo(container, blob.Name),
            SizeBytes = blob.Length,
            LastModified = blob.LastModified.UtcDateTime,
            Version = blob.Version,
            Metadata = new Dictionary<string, string>(blob.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    public static StorageAuditFinding Missing(StorageContainer container, string blobName)
    {
        return new StorageAuditFinding
        {
            Container = container,
            BlobName = blobName,
            Kind = StorageFindingKind.Missing,
            RepoId = BlobNames.OwningRepo(container, blobName)
        };
    }
}


public enum StorageFindingKind
{
    /// <summary>Stored, recognised, and nothing refers to it.</summary>
    Unreferenced,

    /// <summary>Referred to, and not stored.</summary>
    Missing,

    /// <summary>Stored under a name that does not parse.</summary>
    Unrecognised
}
