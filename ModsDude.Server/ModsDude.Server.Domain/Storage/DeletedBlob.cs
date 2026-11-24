namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// A blob the deletion run deleted, with everything it knew about it. The list to restore from while
/// storage still keeps the blob.
/// </summary>
public class DeletedBlob
{
    private DeletedBlob()
    {
    }

    public DeletedBlob(BlobDeletionCandidate candidate, StoredBlob blob, DateTime deleted)
    {
        Container = candidate.Container;
        BlobName = blob.Name;
        SizeBytes = blob.Length;
        LastModified = blob.LastModified.UtcDateTime;
        Metadata = new Dictionary<string, string>(blob.Metadata, StringComparer.OrdinalIgnoreCase);
        Reason = candidate.Reason;
        RequestedBy = candidate.RequestedBy;
        Deleted = deleted;
    }


    public long Id { get; private set; }
    public StorageContainer Container { get; private set; }
    public string BlobName { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTime LastModified { get; private set; }
    public IReadOnlyDictionary<string, string> Metadata { get; private set; } = new Dictionary<string, string>();
    public BlobDeletionReason Reason { get; private set; }
    public string? RequestedBy { get; private set; }
    public DateTime Deleted { get; private set; }
}
