using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

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
/// <item>
/// Uploads: <c>uploads/{repoId}/{userId}/{uploadId}</c> in the mods or savegames container, the only
/// names an upload link is ever issued for. Nothing refers to one; registering or checking in copies it
/// to its address.
/// </item>
/// </list>
/// A name that does not parse is never deleted: nothing can prove it is garbage.
/// </remarks>
public static class BlobNames
{
    private const string _uploadPrefix = "uploads";


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

    public static string Upload(RepoId repoId, UserId userId, UploadId uploadId)
    {
        return $"{_uploadPrefix}/{repoId.Value}/{BlobPathSegment.Validated(userId.Value)}/{uploadId.Value}";
    }

    public static bool IsRecognised(StorageContainer container, string name)
    {
        return container switch
        {
            StorageContainer.Mods => TryParseMod(name, out _) || TryParseUpload(name, out _),
            StorageContainer.Savegames => TryParseSavegame(name, out _) || TryParseUpload(name, out _),
            StorageContainer.Images => TryParseImage(name, out _),
            _ => throw new ArgumentOutOfRangeException(nameof(container), container, null)
        };
    }

    /// <summary>The repo a blob belongs to, or null for images and for names that do not parse.</summary>
    public static RepoId? OwningRepo(StorageContainer container, string name)
    {
        return container switch
        {
            StorageContainer.Mods => TryParseMod(name, out var mod) ? mod.RepoId : TryParseUpload(name, out var upload) ? upload.RepoId : null,
            StorageContainer.Savegames => TryParseSavegame(name, out var savegame) ? savegame.RepoId : TryParseUpload(name, out var upload) ? upload.RepoId : null,
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

    public static bool TryParseUpload(string name, out UploadBlobAddress address)
    {
        address = default;

        var segments = name.Split('/');

        if (segments.Length != 4
            || segments[0] != _uploadPrefix
            || !Guid.TryParse(segments[1], out var repoId)
            || segments[1] != repoId.ToString()
            || !BlobPathSegment.IsValid(segments[2])
            || !Guid.TryParse(segments[3], out var uploadId)
            || segments[3] != uploadId.ToString())
        {
            return false;
        }

        address = new UploadBlobAddress(new RepoId(repoId), new UserId(segments[2]), new UploadId(uploadId));

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

public readonly record struct UploadBlobAddress(RepoId RepoId, UserId UserId, UploadId UploadId);
