using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModsDude.Server.Api.Maintenance;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Storage;

namespace ModsDude.Server.Api.Admin;

public class StoragePageModel(
    ApplicationDbContext dbContext,
    IStorageMaintenance maintenance,
    IBackgroundJobClient backgroundJobs,
    IOptions<StorageMaintenanceOptions> options,
    ILogger<StoragePageModel> logger)
    : PageModel
{
    public StorageInspection? Inspection { get; private set; }
    public StorageMaintenanceStatus? Status { get; private set; }
    public int MinimumSoftDeleteDays => options.Value.MinimumSoftDeleteDays;

    public StorageView View { get; private set; }
    public StorageFilter Filter { get; private set; } = new(null, null, null, StorageSort.Name);
    public IReadOnlyDictionary<StorageView, int> Counts { get; private set; } = new Dictionary<StorageView, int>();

    public IReadOnlyList<FilterOption> RepoOptions { get; private set; } = [];
    public IReadOnlyList<string> GameOptions { get; private set; } = [];

    public AdminPage<FindingRow>? Findings { get; private set; }
    public AdminPage<QueueRow>? Queue { get; private set; }
    public AdminPage<DeletedRow>? Deleted { get; private set; }


    public async Task OnGetAsync(StorageView view, StorageContainer? container, Guid? repo, string? game, StorageSort sort, int page, CancellationToken cancellationToken)
    {
        View = view;
        Filter = new StorageFilter(container, repo, string.IsNullOrWhiteSpace(game) ? null : game, sort);

        try
        {
            Inspection = await maintenance.InspectAsync(options.Value.MinimumSoftDeleteDays, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not inspect storage for the admin page.");
        }

        Status = await dbContext.StorageMaintenanceStatuses.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        Counts = await CountAsync(cancellationToken);

        var rows = new StorageRows(dbContext);

        switch (view)
        {
            case StorageView.Unreferenced or StorageView.Missing or StorageView.Unrecognised:
                var findings = await rows.FindingsAsync(Kind(view), cancellationToken);
                Options(findings.Select(x => (x.Repo, x.Game)));
                Findings = AdminPage<FindingRow>.Of(Sorted(findings.Where(x => Filter.Matches(x.Container, x.Repo, x.Game)), x => x.BlobName, x => x.SizeBytes, x => x.LastModified), page);
                break;

            case StorageView.Queued:
                var queue = await rows.QueueAsync(cancellationToken);
                Options(queue.Select(x => (x.Repo, x.Game)));
                Queue = AdminPage<QueueRow>.Of(Sorted(queue.Where(x => Filter.Matches(x.Container, x.Repo, x.Game)), x => x.BlobName, _ => 0, x => x.Queue.QueuedAt), page);
                break;

            case StorageView.Deleted:
                var deleted = await rows.DeletedAsync(cancellationToken);
                Options(deleted.Select(x => (x.Repo, x.Game)));
                Deleted = AdminPage<DeletedRow>.Of(Sorted(deleted.Where(x => Filter.Matches(x.Container, x.Repo, x.Game)), x => x.BlobName, x => x.SizeBytes, x => x.Deleted), page);
                break;
        }
    }

    public IActionResult OnPostRun()
    {
        backgroundJobs.Enqueue<StorageMaintenanceJob>(x => x.RunAsync(CancellationToken.None));
        logger.LogInformation("Admin {Operator} started storage maintenance.", User.OperatorName());

        TempData.SetNotice("Storage maintenance started.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBindAsync(CancellationToken cancellationToken)
    {
        var failures = await maintenance.BindByHandAsync(User.OperatorName(), cancellationToken);

        if (failures.Count > 0)
        {
            TempData.SetError(string.Join(" ", failures));
        }
        else
        {
            TempData.SetNotice("Storage is bound to this database.");
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long[] ids, StorageContainer? container, Guid? repo, string? game, StorageSort sort, int page, CancellationToken cancellationToken)
    {
        var back = new { view = StorageView.Unreferenced, container, repo, game, sort, page };

        if (ids.Length == 0)
        {
            TempData.SetError("Nothing was selected.");
            return RedirectToPage(back);
        }

        var queued = await maintenance.QueueForDeletionAsync(ids, User.OperatorName(), cancellationToken);
        backgroundJobs.Enqueue<StorageMaintenanceJob>(x => x.RunAsync(CancellationToken.None));

        TempData.SetNotice($"Queued {AdminFormat.Count(queued.Count)} blobs ({AdminFormat.Bytes(queued.Bytes)}) for deletion. Storage maintenance started.");
        return RedirectToPage(back);
    }


    public string? Link(StorageView view) => Url.Page(null, new { view });

    public string? Link(int page) => Url.Page(null, new { view = View, container = Filter.Container, repo = Filter.Repo, game = Filter.Game, sort = Filter.Sort, page });


    private async Task<IReadOnlyDictionary<StorageView, int>> CountAsync(CancellationToken cancellationToken)
    {
        var findings = await dbContext.StorageAuditFindings
            .AsNoTracking()
            .GroupBy(x => x.Kind)
            .Select(x => new { x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        return new Dictionary<StorageView, int>
        {
            [StorageView.Unreferenced] = findings.GetValueOrDefault(StorageFindingKind.Unreferenced),
            [StorageView.Missing] = findings.GetValueOrDefault(StorageFindingKind.Missing),
            [StorageView.Unrecognised] = findings.GetValueOrDefault(StorageFindingKind.Unrecognised),
            [StorageView.Queued] = await dbContext.BlobDeletionCandidates.CountAsync(cancellationToken),
            [StorageView.Deleted] = await dbContext.DeletedBlobs.CountAsync(cancellationToken)
        };
    }

    private void Options(IEnumerable<(RepoCell Repo, string? Game)> rows)
    {
        var materialized = rows.ToList();

        RepoOptions = [.. materialized
            .Where(x => x.Repo.Id is not null)
            .GroupBy(x => x.Repo.Id!.Value)
            .Select(x => new FilterOption(x.Key, x.First().Repo.Name!, x.First().Repo.IsDeleted))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)];

        GameOptions = [.. materialized.Select(x => x.Game).OfType<string>().Distinct().Order(StringComparer.Ordinal)];
    }

    private List<T> Sorted<T>(IEnumerable<T> rows, Func<T, string> name, Func<T, long> size, Func<T, DateTime?> time)
    {
        return Filter.Sort switch
        {
            StorageSort.Size => [.. rows.OrderByDescending(size).ThenBy(name, StringComparer.Ordinal)],
            StorageSort.Time => [.. rows.OrderByDescending(time).ThenBy(name, StringComparer.Ordinal)],
            _ => [.. rows.OrderBy(name, StringComparer.Ordinal)]
        };
    }

    private static StorageFindingKind Kind(StorageView view) => view switch
    {
        StorageView.Unreferenced => StorageFindingKind.Unreferenced,
        StorageView.Missing => StorageFindingKind.Missing,
        StorageView.Unrecognised => StorageFindingKind.Unrecognised,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, null)
    };
}


public enum StorageView
{
    Unreferenced,
    Missing,
    Unrecognised,
    Queued,
    Deleted
}

public enum StorageSort
{
    Name,
    Size,
    Time
}

public sealed record FilterOption(Guid Id, string Name, bool IsDeleted);

public sealed record StorageFilter(StorageContainer? Container, Guid? Repo, string? Game, StorageSort Sort)
{
    public bool Matches(StorageContainer container, RepoCell repo, string? game)
    {
        return (Container is null || Container == container)
            && (Repo is null || Repo == repo.Id)
            && (Game is null || Game == game);
    }
}
