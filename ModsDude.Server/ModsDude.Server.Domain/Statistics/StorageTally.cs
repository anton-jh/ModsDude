using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Statistics;

/// <summary>
/// Adds up container listings per repo as they stream in, then turns the totals into one day's samples.
/// </summary>
public class StorageTally
{
    private readonly Dictionary<(StorageContainer Container, RepoId? RepoId), StoredTotal> _stored = [];


    public void Add(StorageContainer container, StoredBlob blob)
    {
        var key = (container, OwningRepo(container, blob.Name));
        var total = _stored.GetValueOrDefault(key);

        _stored[key] = new StoredTotal(total.Bytes + blob.Length, total.Count + 1);
    }

    /// <param name="registeredModBytes">The bytes of every registered mod version, per repo.</param>
    /// <param name="registeredSavegameBytes">The bytes of every distinct savegame blob address, per repo.</param>
    public IReadOnlyList<StorageUsageSample> ToSamples(
        DateOnly date,
        IReadOnlyDictionary<RepoId, long> registeredModBytes,
        IReadOnlyDictionary<RepoId, long> registeredSavegameBytes)
    {
        var keys = _stored.Keys
            .Concat(registeredModBytes.Keys.Select(x => (StorageContainer.Mods, (RepoId?)x)))
            .Concat(registeredSavegameBytes.Keys.Select(x => (StorageContainer.Savegames, (RepoId?)x)))
            .Append((StorageContainer.Images, null))
            .Distinct()
            .OrderBy(x => x.Item1)
            .ThenBy(x => x.Item2 is not null)
            .ThenBy(x => x.Item2?.Value);

        return [.. keys.Select(key =>
        {
            var (container, repoId) = key;
            var stored = _stored.GetValueOrDefault(key);

            return new StorageUsageSample(
                date,
                container,
                repoId,
                stored.Bytes,
                stored.Count,
                Registered(container, repoId, registeredModBytes, registeredSavegameBytes));
        })];
    }


    private static RepoId? OwningRepo(StorageContainer container, string blobName)
    {
        return container switch
        {
            StorageContainer.Mods => BlobReclamation.TryParseModBlobName(blobName, out var mod) ? mod.RepoId : null,
            StorageContainer.Savegames => BlobReclamation.TryParseSavegameBlobName(blobName, out var savegame) ? savegame.RepoId : null,
            StorageContainer.Images => null,
            _ => throw new ArgumentOutOfRangeException(nameof(container), container, null)
        };
    }

    private static long? Registered(
        StorageContainer container,
        RepoId? repoId,
        IReadOnlyDictionary<RepoId, long> registeredModBytes,
        IReadOnlyDictionary<RepoId, long> registeredSavegameBytes)
    {
        if (container is StorageContainer.Images)
        {
            return null;
        }

        if (repoId is not RepoId owner)
        {
            return 0;
        }

        var registered = container is StorageContainer.Mods ? registeredModBytes : registeredSavegameBytes;

        return registered.GetValueOrDefault(owner);
    }


    private readonly record struct StoredTotal(long Bytes, int Count);
}
