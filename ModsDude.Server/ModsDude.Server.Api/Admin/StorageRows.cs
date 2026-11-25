using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Domain.Users;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Api.Admin;

/// <summary>
/// The Storage page's rows, with everything known about each blob: what its name says, what its label
/// says, what the database says about its repo, and where it stands in the deletion queue.
/// </summary>
public class StorageRows(ApplicationDbContext dbContext)
{
    private static readonly IReadOnlyDictionary<string, string> _captions = new Dictionary<string, string>
    {
        [BlobLabel.RepoNameKey] = "Repo",
        [BlobLabel.RepoIdKey] = "Repo id",
        [BlobLabel.GameKey] = "Game",
        [BlobLabel.ModIdKey] = "Mod id",
        [BlobLabel.VersionIdKey] = "Version id",
        [BlobLabel.DisplayNameKey] = "Display name",
        [BlobLabel.FileNameKey] = "File name",
        [BlobLabel.SavegameNameKey] = "Savegame",
        [BlobLabel.SavegameIdKey] = "Savegame id",
        [BlobLabel.AddedByNameKey] = "Added by",
        [BlobLabel.AddedByKey] = "Added by id",
        [BlobLabel.AddedAtKey] = "Added",
        ["sha256"] = "SHA-256"
    };


    public async Task<IReadOnlyList<FindingRow>> FindingsAsync(StorageFindingKind kind, CancellationToken cancellationToken)
    {
        var findings = await dbContext.StorageAuditFindings
            .AsNoTracking()
            .Where(x => x.Kind == kind)
            .ToListAsync(cancellationToken);

        var repos = await ReposAsync(findings.Select(x => x.RepoId), cancellationToken);
        var queue = (await dbContext.BlobDeletionCandidates.AsNoTracking().ToListAsync(cancellationToken))
            .ToDictionary(x => (x.Container, x.BlobName));
        var users = await UserNamesAsync(queue.Values.Select(x => x.UserId), cancellationToken);

        var rows = new List<FindingRow>();

        foreach (var finding in findings)
        {
            var label = BlobLabel.Read(finding.Metadata);
            var labelled = label.ToDictionary(x => x.Key, x => x.Value);
            var repo = RepoCell(finding.RepoId, repos, labelled);

            rows.Add(new FindingRow(
                finding.Id,
                finding.Container,
                finding.BlobName,
                Title(finding.Container, finding.BlobName, labelled),
                finding.SizeBytes,
                finding.LastModified,
                finding.Version,
                repo,
                repo.Game ?? labelled.GetValueOrDefault(BlobLabel.GameKey),
                [.. label.Select(x => new Fact(Caption(x.Key), x.Value))],
                Address(finding.Container, finding.BlobName),
                queue.TryGetValue((finding.Container, finding.BlobName), out var candidate) ? QueueCell(candidate, users) : null,
                kind is StorageFindingKind.Missing ? await ReferrersAsync(finding.Container, finding.BlobName, cancellationToken) : []));
        }

        return rows;
    }

    public async Task<IReadOnlyList<QueueRow>> QueueAsync(CancellationToken cancellationToken)
    {
        var candidates = await dbContext.BlobDeletionCandidates.AsNoTracking().ToListAsync(cancellationToken);
        var repos = await ReposAsync(candidates.Select(x => x.RepoId), cancellationToken);
        var users = await UserNamesAsync(candidates.Select(x => x.UserId), cancellationToken);

        return [.. candidates.Select(x =>
        {
            var repo = RepoCell(x.RepoId, repos, new Dictionary<string, string>());

            return new QueueRow(x.Container, x.BlobName, repo, repo.Game, Address(x.Container, x.BlobName), QueueCell(x, users));
        })];
    }

    public async Task<IReadOnlyList<DeletedRow>> DeletedAsync(CancellationToken cancellationToken)
    {
        var deleted = await dbContext.DeletedBlobs.AsNoTracking().ToListAsync(cancellationToken);
        var repos = await ReposAsync(deleted.Select(x => BlobNames.OwningRepo(x.Container, x.BlobName)), cancellationToken);

        return [.. deleted.Select(x =>
        {
            var label = BlobLabel.Read(x.Metadata);
            var labelled = label.ToDictionary(y => y.Key, y => y.Value);
            var repo = RepoCell(BlobNames.OwningRepo(x.Container, x.BlobName), repos, labelled);

            return new DeletedRow(
                x.Id,
                x.Container,
                x.BlobName,
                Title(x.Container, x.BlobName, labelled),
                x.SizeBytes,
                x.LastModified,
                repo,
                repo.Game ?? labelled.GetValueOrDefault(BlobLabel.GameKey),
                [.. label.Select(y => new Fact(Caption(y.Key), y.Value))],
                Address(x.Container, x.BlobName),
                Reason(x.Reason),
                x.RequestedBy,
                x.Deleted);
        })];
    }


    private async Task<Dictionary<RepoId, CurrentRepo>> ReposAsync(IEnumerable<RepoId?> repoIds, CancellationToken cancellationToken)
    {
        var ids = repoIds.OfType<RepoId>().Distinct().ToList();

        return (await dbContext.Repos
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.Name, Game = x.AdapterData.Id })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => new CurrentRepo(AdminFormat.Repo(x.Id, x.Name), x.Game.Value));
    }

    private async Task<Dictionary<UserId, string>> UserNamesAsync(IEnumerable<UserId?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.OfType<UserId>().Distinct().ToList();

        return (await dbContext.Users
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.DisplayName })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => AdminFormat.User(x.Id, x.DisplayName));
    }

    private QueueCell QueueCell(BlobDeletionCandidate candidate, IReadOnlyDictionary<UserId, string> users)
    {
        var queuedBy = candidate.RequestedBy
            ?? (candidate.UserId is UserId userId ? users.GetValueOrDefault(userId, userId.Value) : null);

        return new QueueCell(Reason(candidate.Reason), queuedBy, candidate.QueuedAt);
    }

    /// <summary>What refers to a missing blob: the rows a person would have to restore it for.</summary>
    private async Task<IReadOnlyList<string>> ReferrersAsync(StorageContainer container, string blobName, CancellationToken cancellationToken)
    {
        switch (container)
        {
            case StorageContainer.Mods when BlobNames.TryParseMod(blobName, out var mod):
                return await dbContext.ModVersions
                    .AsNoTracking()
                    .Where(x => x.RepoId == mod.RepoId && x.ModId == mod.ModId && x.Id == mod.VersionId)
                    .Select(x => $"Mod version {x.DisplayName} ({x.FileName})")
                    .ToListAsync(cancellationToken);

            case StorageContainer.Savegames when BlobNames.TryParseSavegame(blobName, out var savegame):
                var numbers = await dbContext.SavegameSnapshots
                    .AsNoTracking()
                    .Where(x => x.RepoId == savegame.RepoId && x.SavegameId == savegame.SavegameId && x.ContentHash == savegame.ContentHash)
                    .Select(x => x.Number)
                    .ToListAsync(cancellationToken);
                var name = await dbContext.Savegames
                    .AsNoTracking()
                    .Where(x => x.RepoId == savegame.RepoId && x.Id == savegame.SavegameId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                return [$"Savegame {name.Value}, snapshots {string.Join(", ", numbers.Select(x => x.Value).Order())}"];

            case StorageContainer.Images when BlobNames.TryParseImage(blobName, out var hash):
                var versions = await dbContext.ModVersions.CountAsync(x => x.Images.Any(y => y.Hash == hash), cancellationToken);
                var avatars = await dbContext.Users.CountAsync(x => x.AvatarHash == hash, cancellationToken);

                return [$"{versions} mod versions", $"{avatars} pictures"];

            default:
                return [];
        }
    }

    private static RepoCell RepoCell(RepoId? repoId, IReadOnlyDictionary<RepoId, CurrentRepo> repos, IReadOnlyDictionary<string, string> label)
    {
        if (repoId is not RepoId id)
        {
            return new RepoCell(null, null, null, false);
        }

        return repos.TryGetValue(id, out var current)
            ? new RepoCell(id.Value, current.Name, current.Game, false)
            : new RepoCell(id.Value, label.GetValueOrDefault(BlobLabel.RepoNameKey) ?? id.Value.ToString(), null, true);
    }

    private static string Title(StorageContainer container, string blobName, IReadOnlyDictionary<string, string> label)
    {
        var fromLabel = container switch
        {
            StorageContainer.Mods => label.GetValueOrDefault(BlobLabel.FileNameKey),
            StorageContainer.Savegames => label.GetValueOrDefault(BlobLabel.SavegameNameKey),
            _ => null
        };

        return fromLabel ?? blobName[(blobName.LastIndexOf('/') + 1)..];
    }

    private static IReadOnlyList<Fact> Address(StorageContainer container, string blobName)
    {
        if (BlobNames.TryParseMod(blobName, out var mod) && container is StorageContainer.Mods)
        {
            return [new("Mod id", mod.ModId.Value), new("Version id", mod.VersionId.Value)];
        }

        if (BlobNames.TryParseSavegame(blobName, out var savegame) && container is StorageContainer.Savegames)
        {
            return [new("Savegame id", savegame.SavegameId.Value.ToString()), new("Content hash", savegame.ContentHash)];
        }

        if (BlobNames.TryParseImage(blobName, out var hash) && container is StorageContainer.Images)
        {
            return [new("Image hash", hash)];
        }

        if (BlobNames.TryParseUpload(blobName, out var upload) && container is StorageContainer.Mods or StorageContainer.Savegames)
        {
            return [new("Uploaded by", upload.UserId.Value), new("Upload id", upload.UploadId.Value.ToString())];
        }

        return [];
    }

    private static string Caption(string key) => _captions.GetValueOrDefault(key, key);

    public static string Reason(BlobDeletionReason reason) => reason switch
    {
        BlobDeletionReason.ReferenceRemoved => "No longer referred to",
        BlobDeletionReason.UploadIssued => "Uploaded",
        BlobDeletionReason.Admin => "Chosen by an admin",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };


    private sealed record CurrentRepo(string Name, string Game);
}


public sealed record Fact(string Caption, string Value);

/// <param name="Name">The repo as it is now, or as the blob's label names it when it is gone.</param>
/// <param name="IsDeleted">Whether the repo no longer exists.</param>
public sealed record RepoCell(Guid? Id, string? Name, string? Game, bool IsDeleted);

public sealed record QueueCell(string Reason, string? QueuedBy, DateTime QueuedAt);

public sealed record FindingRow(
    long Id,
    StorageContainer Container,
    string BlobName,
    string Title,
    long SizeBytes,
    DateTime? LastModified,
    string? Version,
    RepoCell Repo,
    string? Game,
    IReadOnlyList<Fact> Label,
    IReadOnlyList<Fact> Address,
    QueueCell? Queue,
    IReadOnlyList<string> Referrers);

public sealed record QueueRow(
    StorageContainer Container,
    string BlobName,
    RepoCell Repo,
    string? Game,
    IReadOnlyList<Fact> Address,
    QueueCell Queue);

public sealed record DeletedRow(
    long Id,
    StorageContainer Container,
    string BlobName,
    string Title,
    long SizeBytes,
    DateTime LastModified,
    RepoCell Repo,
    string? Game,
    IReadOnlyList<Fact> Label,
    IReadOnlyList<Fact> Address,
    string Reason,
    string? RequestedBy,
    DateTime Deleted);
