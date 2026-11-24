using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// The name every blob is stored under, built and read in this one place. The deletion triggers build
/// the same names in SQL; the persistence tests hold the two to each other.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Mods: <c>{repoId}/{modId}/{versionId}</c>, one blob per version.</item>
/// <item>Savegames: <c>{repoId}/{savegameId}/{contentHash}</c>, shared by every snapshot of the savegame with those bytes.</item>
/// <item>Images: <c>{hash[..2]}/{hash}</c>, shared by every repo.</item>
/// </list>
/// A name that does not parse is never deleted: nothing can prove it is garbage.
/// </remarks>
public static class BlobNames
{
    public static string Mod(RepoId repoId, ModId modId, ModVersionId versionId)
    {
        return $"{repoId.Value}/{BlobPathSegment.Validated(modId.Value)}/{BlobPathSegment.Validated(versionId.Value)}";
    }

    public static string Savegame(RepoId repoId, SavegameId savegameId, string contentHash)
    {
        return $"{repoId.Value}/{savegameId.Value}/{Sha256Hex.Validated(contentHash)}";
    }

    public static string Image(string hash)
    {
        var validated = Sha256Hex.Validated(hash);

        return $"{validated[..2]}/{validated}";
    }

    public static bool IsRecognised(StorageContainer container, string name)
    {
        return container switch
        {
            StorageContainer.Mods => TryParseMod(name, out _),
            StorageContainer.Savegames => TryParseSavegame(name, out _),
            StorageContainer.Images => TryParseImage(name, out _),
            _ => throw new ArgumentOutOfRangeException(nameof(container), container, null)
        };
    }

    /// <summary>The repo a blob belongs to, or null for images and for names that do not parse.</summary>
    public static RepoId? OwningRepo(StorageContainer container, string name)
    {
        return container switch
        {
            StorageContainer.Mods => TryParseMod(name, out var mod) ? mod.RepoId : null,
            StorageContainer.Savegames => TryParseSavegame(name, out var savegame) ? savegame.RepoId : null,
            StorageContainer.Images => null,
            _ => throw new ArgumentOutOfRangeException(nameof(container), container, null)
        };
    }

    public static bool TryParseMod(string name, out ModBlobAddress address)
    {
        address = default;

        var segments = name.Split('/');

        if (segments.Length != 3
            || !Guid.TryParse(segments[0], out var repoId)
            || segments[0] != repoId.ToString()
            || !ModId.IsValid(segments[1])
            || !ModVersionId.IsValid(segments[2]))
        {
            return false;
        }

        address = new ModBlobAddress(new RepoId(repoId), new ModId(segments[1]), new ModVersionId(segments[2]));

        return true;
    }

    public static bool TryParseSavegame(string name, out SavegameBlobAddress address)
    {
        address = default;

        var segments = name.Split('/');

        if (segments.Length != 3
            || !Guid.TryParse(segments[0], out var repoId)
            || segments[0] != repoId.ToString()
            || !Guid.TryParse(segments[1], out var savegameId)
            || segments[1] != savegameId.ToString()
            || !Sha256Hex.IsValid(segments[2]))
        {
            return false;
        }

        address = new SavegameBlobAddress(new RepoId(repoId), new SavegameId(savegameId), segments[2]);

        return true;
    }

    public static bool TryParseImage(string name, out string hash)
    {
        hash = string.Empty;

        var segments = name.Split('/');

        if (segments.Length != 2
            || !Sha256Hex.IsValid(segments[1])
            || segments[0] != segments[1][..2])
        {
            return false;
        }

        hash = segments[1];

        return true;
    }
}


public readonly record struct ModBlobAddress(RepoId RepoId, ModId ModId, ModVersionId VersionId);

public readonly record struct SavegameBlobAddress(RepoId RepoId, SavegameId SavegameId, string ContentHash);
