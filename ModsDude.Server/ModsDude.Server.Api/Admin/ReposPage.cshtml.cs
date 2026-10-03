using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.RepoMemberships;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Admin;

public class ReposPageModel(
    ApplicationDbContext dbContext,
    ITimeService timeService)
    : PageModel
{
    public StorageComparison? Storage { get; private set; }

    public IReadOnlyList<RepoRow> Repos { get; private set; } = [];


    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var repos = await dbContext.Repos
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .ToListAsync(cancellationToken);

        var users = await dbContext.Users
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var memberships = (await dbContext.RepoMemberships
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .ToLookup(x => x.RepoId);

        var mods = await dbContext.ModVersions
            .AsNoTracking()
            .GroupBy(x => x.RepoId)
            .Select(x => new { RepoId = x.Key, Mods = x.Select(y => y.ModId).Distinct().Count(), Versions = x.Count() })
            .ToDictionaryAsync(x => x.RepoId, cancellationToken);

        var savegames = await dbContext.Savegames
            .AsNoTracking()
            .GroupBy(x => x.RepoId)
            .Select(x => new { RepoId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.RepoId, x => x.Count, cancellationToken);

        var modBytes = await dbContext.ModVersions.GetRegisteredBytesPerRepoAsync(cancellationToken);
        var savegameBytes = await dbContext.SavegameSnapshots.GetRegisteredBytesPerRepoAsync(cancellationToken);
        var scheduledMods = await dbContext.ModVersions.GetScheduledForDeletionBytesPerRepoAsync(cancellationToken);
        var scheduledSavegames = await dbContext.SavegameSnapshots.GetScheduledForDeletionBytesPerRepoAsync(cancellationToken);

        var traffic = (await dbContext.FileTransfers.GetTotalsSinceAsync(timeService.Now().AddDays(-AdminWindows.TrafficDays), cancellationToken))
            .ToLookup(x => x.RepoId);

        Storage = await StorageComparison.LoadAsync(dbContext, cancellationToken);

        Repos = [.. repos
            .OrderBy(x => x.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id.Value)
            .Select(repo => new RepoRow(
                AdminFormat.Repo(repo.Id, repo.Name),
                repo.AdapterData.Id.Value,
                repo.Created,
                repo.ArchivedAt,
                [.. memberships[repo.Id]
                    .OrderByDescending(x => x.Level)
                    .ThenBy(x => users[x.UserId].DisplayName.Value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.UserId.Value, StringComparer.Ordinal)
                    .Select(x => new MemberRow(AdminFormat.User(x.UserId, users[x.UserId].DisplayName), x.Level))],
                mods.GetValueOrDefault(repo.Id)?.Mods ?? 0,
                mods.GetValueOrDefault(repo.Id)?.Versions ?? 0,
                savegames.GetValueOrDefault(repo.Id),
                modBytes.GetValueOrDefault(repo.Id),
                savegameBytes.GetValueOrDefault(repo.Id),
                Storage?.ForRepo(repo.Id),
                scheduledMods.GetValueOrDefault(repo.Id) + scheduledSavegames.GetValueOrDefault(repo.Id),
                TrafficRow.PerFile(traffic[repo.Id]),
                TrafficRow.DownloadedBytesOf(traffic[repo.Id]),
                TrafficRow.UploadedBytesOf(traffic[repo.Id])))];
    }


    /// <param name="ModBytes">Every mod version the repo has registered.</param>
    /// <param name="SavegameBytes">Every distinct file its savegame snapshots refer to.</param>
    /// <param name="Storage">From the latest storage sample, or <c>null</c> before the first one.</param>
    public record RepoRow(
        string Name,
        string Game,
        DateTime Created,
        DateTime? ArchivedAt,
        IReadOnlyList<MemberRow> Members,
        int ModCount,
        int VersionCount,
        int SavegameCount,
        long ModBytes,
        long SavegameBytes,
        RepoStorage? Storage,
        long ScheduledForDeletionBytes,
        IReadOnlyList<TrafficRow> Traffic,
        long DownloadedBytes,
        long UploadedBytes);

    public record MemberRow(string User, RepoMembershipLevel Level);
}
