using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// Which blobs the database refers to, as blob names built by <see cref="BlobNames"/>: mod versions
/// refer to mod blobs, savegame snapshots to savegame blobs, and image references and avatars to images.
/// Nothing refers to an upload.
/// </summary>
public static class BlobReferences
{
    /// <summary>Every name the database refers to in <paramref name="container"/>.</summary>
    /// <remarks>Read after the container is listed, never before; see <see cref="BlobAudit"/>.</remarks>
    public static async Task<IReadOnlySet<string>> GetReferencedNamesAsync(
        this ApplicationDbContext dbContext,
        StorageContainer container,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> names = container switch
        {
            StorageContainer.Mods => (await dbContext.ModVersions
                    .AsNoTracking()
                    .Select(x => new { x.RepoId, x.ModId, x.Id })
                    .ToListAsync(cancellationToken))
                .Select(x => BlobNames.Mod(x.RepoId, x.ModId, x.Id)),

            StorageContainer.Savegames => (await dbContext.SavegameSnapshots
                    .AsNoTracking()
                    .Select(x => new { x.RepoId, x.SavegameId, x.ContentHash })
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .Select(x => BlobNames.Savegame(x.RepoId, x.SavegameId, x.ContentHash)),

            StorageContainer.Images => (await ReferencedImageHashesAsync(dbContext, cancellationToken))
                .Select(BlobNames.Image),

            _ => throw new ArgumentOutOfRangeException(nameof(container), container, null)
        };

        return names.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Whether anything refers to one blob. A name that does not parse is treated as referred to.</summary>
    public static async Task<bool> IsReferencedAsync(
        this ApplicationDbContext dbContext,
        StorageContainer container,
        string blobName,
        CancellationToken cancellationToken)
    {
        switch (container)
        {
            case StorageContainer.Mods when BlobNames.TryParseMod(blobName, out var mod):
                return await dbContext.ModVersions
                    .AnyAsync(x => x.RepoId == mod.RepoId && x.ModId == mod.ModId && x.Id == mod.VersionId, cancellationToken);

            case StorageContainer.Savegames when BlobNames.TryParseSavegame(blobName, out var savegame):
                return await dbContext.SavegameSnapshots
                    .AnyAsync(x => x.RepoId == savegame.RepoId && x.SavegameId == savegame.SavegameId && x.ContentHash == savegame.ContentHash, cancellationToken);

            case StorageContainer.Images when BlobNames.TryParseImage(blobName, out var hash):
                return await dbContext.ModVersions.AnyAsync(x => x.Images.Any(y => y.Hash == hash), cancellationToken)
                    || await dbContext.Users.AnyAsync(x => x.AvatarHash == hash, cancellationToken);

            case StorageContainer.Mods or StorageContainer.Savegames when BlobNames.TryParseUpload(blobName, out _):
                return false;

            default:
                return true;
        }
    }

    /// <summary>
    /// The label each named blob would have been given when it was claimed, built from what the
    /// database holds now. Names nothing refers to any more are left out. Who added a mod version was
    /// never recorded, so a mod blob's label names nobody.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, BlobLabel>> GetLabelsAsync(
        this ApplicationDbContext dbContext,
        StorageContainer container,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken)
    {
        return container switch
        {
            StorageContainer.Mods => await GetModLabelsAsync(dbContext, blobNames, cancellationToken),
            StorageContainer.Savegames => await GetSavegameLabelsAsync(dbContext, blobNames, cancellationToken),
            StorageContainer.Images => new Dictionary<string, BlobLabel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(container), container, null)
        };
    }


    private static async Task<IReadOnlyList<string>> ReferencedImageHashesAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var versionImages = await dbContext.ModVersions
            .AsNoTracking()
            .SelectMany(x => x.Images)
            .Select(x => x.Hash)
            .Distinct()
            .ToListAsync(cancellationToken);

        var avatars = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.AvatarHash != null)
            .Select(x => x.AvatarHash!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. versionImages.Union(avatars, StringComparer.Ordinal)];
    }

    private static async Task<Dictionary<RepoId, RepoFacts>> GetRepoFactsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<RepoId> repoIds,
        CancellationToken cancellationToken)
    {
        return (await dbContext.Repos
                .AsNoTracking()
                .Where(x => repoIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, Game = x.AdapterData.Id })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => new RepoFacts(x.Name.Value, x.Game));
    }

    private static async Task<IReadOnlyDictionary<string, BlobLabel>> GetModLabelsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken)
    {
        var addresses = blobNames
            .Select(x => BlobNames.TryParseMod(x, out var address) ? address : (ModBlobAddress?)null)
            .OfType<ModBlobAddress>()
            .ToHashSet();

        var repoIds = addresses.Select(x => x.RepoId).Distinct().ToList();
        var repos = await GetRepoFactsAsync(dbContext, repoIds, cancellationToken);

        var versions = await dbContext.ModVersions
            .AsNoTracking()
            .Where(x => repoIds.Contains(x.RepoId))
            .Select(x => new { x.RepoId, x.ModId, x.Id, x.DisplayName, x.FileName, x.Created })
            .ToListAsync(cancellationToken);

        return versions
            .Where(x => addresses.Contains(new ModBlobAddress(x.RepoId, x.ModId, x.Id)) && repos.ContainsKey(x.RepoId))
            .ToDictionary(
                x => BlobNames.Mod(x.RepoId, x.ModId, x.Id),
                x => (BlobLabel)new ModBlobLabel(
                    x.RepoId,
                    repos[x.RepoId].Name,
                    repos[x.RepoId].Game,
                    null,
                    null,
                    x.Created.UtcDateTime,
                    x.ModId,
                    x.Id,
                    x.DisplayName,
                    x.FileName),
                StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, BlobLabel>> GetSavegameLabelsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken)
    {
        var addresses = blobNames
            .Select(x => BlobNames.TryParseSavegame(x, out var address) ? address : (SavegameBlobAddress?)null)
            .OfType<SavegameBlobAddress>()
            .ToHashSet();

        var savegameIds = addresses.Select(x => x.SavegameId).Distinct().ToList();
        var repos = await GetRepoFactsAsync(dbContext, [.. addresses.Select(x => x.RepoId).Distinct()], cancellationToken);

        var savegames = await dbContext.Savegames
            .AsNoTracking()
            .Where(x => savegameIds.Contains(x.Id))
            .Select(x => new { x.RepoId, x.Id, x.Name })
            .ToDictionaryAsync(x => (x.RepoId, x.Id), cancellationToken);

        var snapshots = await dbContext.SavegameSnapshots
            .AsNoTracking()
            .Where(x => savegameIds.Contains(x.SavegameId))
            .Select(x => new { x.RepoId, x.SavegameId, x.ContentHash, x.Number, x.CreatedBy, x.Created })
            .ToListAsync(cancellationToken);

        var firsts = snapshots
            .Where(x => addresses.Contains(new SavegameBlobAddress(x.RepoId, x.SavegameId, x.ContentHash)))
            .GroupBy(x => new SavegameBlobAddress(x.RepoId, x.SavegameId, x.ContentHash))
            .Select(x => x.OrderBy(y => y.Number.Value).First())
            .ToList();

        var userIds = firsts.Select(x => x.CreatedBy).Distinct().ToList();
        var users = await dbContext.Users
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName.Value, cancellationToken);

        return firsts
            .Where(x => repos.ContainsKey(x.RepoId) && savegames.ContainsKey((x.RepoId, x.SavegameId)))
            .ToDictionary(
                x => BlobNames.Savegame(x.RepoId, x.SavegameId, x.ContentHash),
                x => (BlobLabel)new SavegameBlobLabel(
                    x.RepoId,
                    repos[x.RepoId].Name,
                    repos[x.RepoId].Game,
                    x.CreatedBy,
                    users.GetValueOrDefault(x.CreatedBy),
                    x.Created,
                    x.SavegameId,
                    savegames[(x.RepoId, x.SavegameId)].Name.Value),
                StringComparer.Ordinal);
    }


    private sealed record RepoFacts(string Name, AdapterIdentifier Game);
}
