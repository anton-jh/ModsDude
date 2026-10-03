using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Invites;
using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Admin;

public class OverviewPageModel(
    ApplicationDbContext dbContext,
    ITimeService timeService)
    : PageModel
{
    private const int _largestListed = 10;


    public int UserCount { get; private set; }
    public int ActiveShortUserCount { get; private set; }
    public int ActiveLongUserCount { get; private set; }
    public int TrustedUserCount { get; private set; }
    public int RepoCount { get; private set; }
    public int ArchivedRepoCount { get; private set; }
    public int OpenInviteCount { get; private set; }

    public DateOnly? StorageSampledOn { get; private set; }
    public IReadOnlyList<ContainerRow> Containers { get; private set; } = [];
    public long ScheduledModBytes { get; private set; }
    public long ScheduledSavegameBytes { get; private set; }
    public StorageTrendChart? Trend { get; private set; }

    public IReadOnlyList<TrafficRow> Traffic { get; private set; } = [];

    public IReadOnlyList<LargestModVersionRow> LargestModVersions { get; private set; } = [];
    public IReadOnlyList<LargestSavegameRow> LargestSavegames { get; private set; } = [];


    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = timeService.Now();

        await LoadUsersAndReposAsync(now, cancellationToken);
        await LoadStorageAsync(cancellationToken);
        await LoadTrafficAsync(now, cancellationToken);
        await LoadLargestAsync(cancellationToken);
    }


    private async Task LoadUsersAndReposAsync(DateTime now, CancellationToken cancellationToken)
    {
        var activeShortSince = now.AddDays(-AdminWindows.ActiveUserShortDays);
        var activeLongSince = now.AddDays(-AdminWindows.ActiveUserLongDays);

        UserCount = await dbContext.Users.CountAsync(cancellationToken);
        ActiveShortUserCount = await dbContext.Users.CountAsync(x => x.LastSeen >= activeShortSince, cancellationToken);
        ActiveLongUserCount = await dbContext.Users.CountAsync(x => x.LastSeen >= activeLongSince, cancellationToken);
        TrustedUserCount = await dbContext.Users.CountAsync(x => x.IsTrusted, cancellationToken);

        RepoCount = await dbContext.Repos.CountAsync(x => x.ArchivedAt == null, cancellationToken);
        ArchivedRepoCount = await dbContext.Repos.CountAsync(x => x.ArchivedAt != null, cancellationToken);

        var invites = await dbContext.RepoInvites
            .AsNoTracking()
            .Where(x => !x.IsRevoked)
            .ToListAsync(cancellationToken);
        OpenInviteCount = invites.Count(x => x.GetStatus(now) is InviteStatus.Active);
    }

    private async Task LoadStorageAsync(CancellationToken cancellationToken)
    {
        var latest = await dbContext.StorageUsageSamples.GetLatestDateAsync(cancellationToken);
        var samples = latest is DateOnly date
            ? await dbContext.StorageUsageSamples.GetOnDateAsync(date, cancellationToken)
            : [];

        StorageSampledOn = latest;
        Containers = [.. samples
            .GroupBy(x => x.Container)
            .OrderBy(x => x.Key)
            .Select(x => new ContainerRow(
                x.Key,
                x.Sum(y => y.StoredBytes),
                x.Sum(y => y.BlobCount),
                x.Any(y => y.RegisteredBytes is null) ? null : x.Sum(y => y.RegisteredBytes ?? 0)))];

        ScheduledModBytes = (await dbContext.ModVersions.GetScheduledForDeletionBytesPerRepoAsync(cancellationToken)).Values.Sum();
        ScheduledSavegameBytes = (await dbContext.SavegameSnapshots.GetScheduledForDeletionBytesPerRepoAsync(cancellationToken)).Values.Sum();

        Trend = latest is DateOnly trendTo
            ? new StorageTrendChart(await dbContext.StorageUsageSamples.GetDailyTotalsFromAsync(trendTo.AddDays(-AdminWindows.StorageTrendDays), cancellationToken))
            : null;
    }

    private async Task LoadTrafficAsync(DateTime now, CancellationToken cancellationToken)
    {
        Traffic = TrafficRow.PerFile(await dbContext.FileTransfers.GetTotalsSinceAsync(now.AddDays(-AdminWindows.TrafficDays), cancellationToken));
    }

    private async Task LoadLargestAsync(CancellationToken cancellationToken)
    {
        var repos = await dbContext.Repos
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .ToDictionaryAsync(x => x.Id, x => AdminFormat.Repo(x.Id, x.Name), cancellationToken);

        var versions = await dbContext.ModVersions
            .AsNoTracking()
            .OrderByDescending(x => x.SizeBytes)
            .ThenBy(x => x.RepoId)
            .ThenBy(x => x.ModId)
            .ThenBy(x => x.Id)
            .Take(_largestListed)
            .Select(x => new { x.RepoId, x.DisplayName, x.Id, x.SizeBytes })
            .ToListAsync(cancellationToken);

        LargestModVersions = [.. versions.Select(x => new LargestModVersionRow(repos[x.RepoId], x.DisplayName, x.Id.Value, x.SizeBytes))];

        var savegames = await dbContext.Savegames
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var addresses = await dbContext.SavegameSnapshots
            .AsNoTracking()
            .GroupBy(x => new { x.SavegameId, x.ContentHash })
            .Select(x => new { x.Key.SavegameId, Bytes = x.Max(y => y.SizeBytes) })
            .ToListAsync(cancellationToken);

        LargestSavegames = [.. addresses
            .GroupBy(x => x.SavegameId)
            .Select(x => new { SavegameId = x.Key, Blobs = x.Count(), Bytes = x.Sum(y => y.Bytes) })
            .OrderByDescending(x => x.Bytes)
            .ThenBy(x => x.SavegameId.Value)
            .Take(_largestListed)
            .Select(x =>
            {
                var savegame = savegames[x.SavegameId];

                return new LargestSavegameRow(repos[savegame.RepoId], savegame.Name.Value, x.Blobs, x.Bytes);
            })];
    }


    /// <param name="RegisteredBytes">What the database accounts for, or <c>null</c> where it does not record sizes.</param>
    public record ContainerRow(StorageContainer Container, long StoredBytes, int BlobCount, long? RegisteredBytes);

    public record LargestModVersionRow(string Repo, string Mod, string Version, long SizeBytes);

    /// <param name="Blobs">Distinct stored files across the savegame's snapshots.</param>
    public record LargestSavegameRow(string Repo, string Savegame, int Blobs, long Bytes);
}
