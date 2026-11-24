using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;
using System.Globalization;

namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// What a person needs to tell a blob apart from the others, written into the blob's metadata when it
/// is first claimed. It describes the blob as it was then and is never rewritten, so a later rename
/// does not reach it; the database has the current names for as long as anything refers to the blob.
/// </summary>
/// <remarks>
/// A savegame blob is shared by every snapshot of its savegame with the same bytes, so its label
/// describes the savegame and whoever first checked those bytes in, never a snapshot.
/// </remarks>
public abstract record BlobLabel(
    RepoId RepoId,
    string RepoName,
    AdapterIdentifier Game,
    UserId? AddedBy,
    string? AddedByName,
    DateTime AddedAt)
{
    public const string RepoIdKey = "repoid";
    public const string RepoNameKey = "reponame";
    public const string GameKey = "game";
    public const string AddedByKey = "addedby";
    public const string AddedByNameKey = "addedbyname";
    public const string AddedAtKey = "addedat";
    public const string ModIdKey = "modid";
    public const string VersionIdKey = "versionid";
    public const string DisplayNameKey = "displayname";
    public const string FileNameKey = "filename";
    public const string SavegameIdKey = "savegameid";
    public const string SavegameNameKey = "savegamename";

    /// <summary>Every key a label writes, in the order a person reads them.</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        RepoNameKey, RepoIdKey, GameKey,
        ModIdKey, VersionIdKey, DisplayNameKey, FileNameKey,
        SavegameNameKey, SavegameIdKey,
        AddedByNameKey, AddedByKey, AddedAtKey
    ];


    public static bool IsPresentIn(IReadOnlyDictionary<string, string> metadata)
    {
        return metadata.Keys.Any(x => string.Equals(x, RepoIdKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The metadata a claim writes: what is there already, plus this label where the blob carries none.
    /// The first label stays, so a blob shared by later snapshots keeps describing who added it.
    /// </summary>
    public IReadOnlyDictionary<string, string> ApplyTo(IReadOnlyDictionary<string, string> metadata)
    {
        var result = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);

        if (IsPresentIn(metadata))
        {
            return result;
        }

        foreach (var (key, value) in Entries())
        {
            result[key] = BlobMetadataText.Encode(value);
        }

        return result;
    }

    /// <summary>
    /// The decoded label entries of a blob's metadata, in <see cref="Keys"/> order, followed by any other
    /// entries it carries in key order. Values are decoded only for label keys.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Read(IReadOnlyDictionary<string, string> metadata)
    {
        var byKey = metadata.ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Value);

        var known = Keys
            .Where(byKey.ContainsKey)
            .Select(x => KeyValuePair.Create(x, BlobMetadataText.Decode(byKey[x])));

        var others = byKey
            .Where(x => !Keys.Contains(x.Key))
            .OrderBy(x => x.Key, StringComparer.Ordinal);

        return [.. known, .. others];
    }


    protected virtual IEnumerable<(string Key, string Value)> Entries()
    {
        yield return (RepoIdKey, RepoId.Value.ToString());
        yield return (RepoNameKey, RepoName);
        yield return (GameKey, Game.Value);

        if (AddedBy is UserId addedBy)
        {
            yield return (AddedByKey, addedBy.Value);
        }

        if (AddedByName is not null)
        {
            yield return (AddedByNameKey, AddedByName);
        }

        yield return (AddedAtKey, AddedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }
}


public sealed record ModBlobLabel(
    RepoId RepoId,
    string RepoName,
    AdapterIdentifier Game,
    UserId? AddedBy,
    string? AddedByName,
    DateTime AddedAt,
    ModId ModId,
    ModVersionId VersionId,
    string DisplayName,
    string FileName)
    : BlobLabel(RepoId, RepoName, Game, AddedBy, AddedByName, AddedAt)
{
    protected override IEnumerable<(string Key, string Value)> Entries()
    {
        return base.Entries().Concat(
        [
            (ModIdKey, ModId.Value),
            (VersionIdKey, VersionId.Value),
            (DisplayNameKey, DisplayName),
            (FileNameKey, FileName)
        ]);
    }
}


public sealed record SavegameBlobLabel(
    RepoId RepoId,
    string RepoName,
    AdapterIdentifier Game,
    UserId? AddedBy,
    string? AddedByName,
    DateTime AddedAt,
    SavegameId SavegameId,
    string SavegameName)
    : BlobLabel(RepoId, RepoName, Game, AddedBy, AddedByName, AddedAt)
{
    protected override IEnumerable<(string Key, string Value)> Entries()
    {
        return base.Entries().Concat(
        [
            (SavegameIdKey, SavegameId.Value.ToString()),
            (SavegameNameKey, SavegameName)
        ]);
    }
}
