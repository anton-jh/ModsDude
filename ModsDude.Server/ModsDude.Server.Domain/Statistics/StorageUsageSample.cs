using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Statistics;

/// <summary>
/// What one container held for one repo on one day.
/// </summary>
/// <remarks>
/// A sample without a repo holds what no repo owns: every image, since images are shared across repos,
/// and any mod or savegame blob whose name does not follow the storage layout.
/// </remarks>
public class StorageUsageSample
{
    // ef
    private StorageUsageSample() { }

    public StorageUsageSample(
        DateOnly date,
        StorageContainer container,
        RepoId? repoId,
        long storedBytes,
        int blobCount,
        long? registeredBytes)
    {
        if (storedBytes < 0 || blobCount < 0 || registeredBytes < 0)
        {
            throw new DomainValidationException("A storage usage sample cannot hold negative amounts.");
        }

        Date = date;
        Container = container;
        RepoId = repoId;
        StoredBytes = storedBytes;
        BlobCount = blobCount;
        RegisteredBytes = registeredBytes;
    }


    public StorageUsageSampleId Id { get; init; } = new(Guid.NewGuid());

    public DateOnly Date { get; private set; }
    public StorageContainer Container { get; private set; }
    public RepoId? RepoId { get; private set; }

    /// <summary>The bytes storage holds.</summary>
    public long StoredBytes { get; private set; }

    public int BlobCount { get; private set; }

    /// <summary>
    /// The bytes the database says should be stored, or <c>null</c> where it does not record sizes
    /// (images). Stored bytes above this are orphans waiting for blob reclamation.
    /// </summary>
    public long? RegisteredBytes { get; private set; }
}


public readonly record struct StorageUsageSampleId(Guid Value);

public enum StorageContainer
{
    Mods,
    Savegames,
    Images
}
