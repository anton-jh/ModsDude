using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Mods;

public class Mod
{
    private readonly HashSet<ModVersion> _versions = [];


    /// <summary>
    /// Only for ef core
    /// </summary>
    private Mod()
    {
    }

    public Mod(
        RepoId repoId,
        ModId id,
        ModVersionId firstVersionId,
        IEnumerable<ModAttribute> attributes,
        DateTimeOffset timestamp,
        string description,
        string displayName)
    {
        RepoId = repoId;
        Id = id;
        Created = timestamp;
        Updated = timestamp;

        AddVersion(
            firstVersionId,
            attributes,
            timestamp,
            description,
            displayName);
    }


    public RepoId RepoId { get; init; } // can i get rid of this? should i? TODO
    public ModId Id { get; init; }
    public DateTimeOffset Created { get; private set; }
    public DateTimeOffset Updated { get; private set; }

    public IReadOnlySet<ModVersion> Versions => _versions;


    public bool CheckHasVersion(ModVersionId versionId)
    {
        return Versions.Any(x => x.Id == versionId);
    }

    public ModVersion GetLatestVersion()
    {
        return _versions
            .OrderByDescending(x => x.SequenceNumber)
            .First();
    }

    public ModVersion AddVersion(
        ModVersionId id,
        IEnumerable<ModAttribute> attributes,
        DateTimeOffset timestamp,
        string description,
        string displayName)
    {
        var newVersion = new ModVersion()
        {
            Id = id,
            Attributes = new(attributes),
            Created = timestamp,
            Description = description,
            DisplayName = displayName,
            Mod = this,
            SequenceNumber = GetNextSequenceNumberForVersion()
        };

        _versions.Add(newVersion);
        Updated = timestamp;

        return newVersion;
    }

    public ModVersion InsertVersion(
        ModVersionId id,
        IEnumerable<ModAttribute> attributes,
        DateTimeOffset timestamp,
        string description,
        string displayName,
        ModVersionId before)
    {
        if (_versions.Any(x => x.Id == id))
        {
            throw new InvalidOperationException($"Cannot insert version with id '{id}'. A version with that id already exists");
        }

        var firstFollowing = _versions.FirstOrDefault(x => x.Id == before)
            ?? throw new InvalidOperationException($"Cannot insert before version with id '{before}'. No such version exists");

        var allFollowing = _versions.Where(x => x.SequenceNumber >= firstFollowing.SequenceNumber);

        foreach (var version in allFollowing)
        {
            version.SequenceNumber++;
        }

        var newVersion = new ModVersion()
        {
            Id = id,
            Attributes = new(attributes),
            Created = timestamp,
            Description = description,
            DisplayName = displayName,
            Mod = this,
            SequenceNumber = firstFollowing.SequenceNumber - 1
        };

        _versions.Add(newVersion);
        Updated = timestamp;

        return newVersion;
    }

    public void RemoveVersion(ModVersionId versionId, DateTimeOffset timestamp)
    {
        var version = _versions.FirstOrDefault(x => x.Id == versionId)
            ?? throw new InvalidOperationException($"Cannot remove version with id '{versionId}'. No such version exists");

        RemoveVersion(version, timestamp);
    }

    public void RemoveVersion(ModVersion version, DateTimeOffset timestamp)
    {
        if (!_versions.Remove(version))
        {
            throw new InvalidOperationException($"Cannot remove version with id '{version.Id}'. No such version exists");
        }

        if (_versions.Count <= 1)
        {
            throw new InvalidOperationException($"Cannot remove only version of a Mod");
        }

        var newerVersions = _versions
            .Where(x => x.SequenceNumber > version.SequenceNumber);

        foreach (var newerVersion in newerVersions)
        {
            newerVersion.SequenceNumber--;
        }

        Updated = timestamp;
    }


    private int GetNextSequenceNumberForVersion()
    {
        return _versions.MaxBy(x => x.SequenceNumber)
            ?.SequenceNumber
            ?? 0;
    }
}


public readonly record struct ModId(string Value);
