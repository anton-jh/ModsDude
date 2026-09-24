using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Maintenance;

/// <summary>
/// Reclaims blobs nothing refers to. Until this existed no code path anywhere deleted a blob except
/// the two delete endpoints, so a failed import — which uploads before it registers — and any version
/// deleted before those endpoints landed stranded its bytes permanently.
/// </summary>
/// <remarks>
/// <para>
/// Deciding what is garbage lives in <see cref="BlobReclamation"/>, which is pure and tested; this
/// class only fetches the two sides of that decision and carries out the deletes. There is no storage
/// emulator to develop against, so the part that can destroy data is deliberately the part that does
/// not need one.
/// </para>
/// <para>
/// <b>The listing is read before the registrations, always.</b> A blob written before the listing and
/// registered while it was running then reads as registered and is kept. Reversed, that same blob
/// would be missing from the registrations and absent from nothing — and would be deleted out from
/// under a live import.
/// </para>
/// <para>
/// <b>A sweep that would empty most of a container deletes nothing at all.</b> Measured against a
/// database that is not the one the storage account belongs to, every blob looks orphaned; see
/// <see cref="ReclamationPlan.IsImplausible"/> and <see cref="BlobReclamationOptions.MaxReclaimableShare"/>.
/// </para>
/// </remarks>
public class BlobReclamationJob(
    ApplicationDbContext dbContext,
    IModStorageService modStorage,
    IModImageStorageService imageStorage,
    ISavegameStorageService savegameStorage,
    ITimeService timeService,
    IOptions<BlobReclamationOptions> options,
    ILogger<BlobReclamationJob> logger)
{
    public const string JobId = "blob-reclamation";


    /// <summary>
    /// Registers the job, or removes it when reclamation is disabled. Idempotent, like
    /// <see cref="RetentionJobs.Register"/>.
    /// </summary>
    /// <remarks>
    /// An occurrence missed while the server was down is skipped rather than caught up on at startup,
    /// so the sweep only ever runs at its scheduled time and a crash loop cannot become a delete loop.
    /// </remarks>
    public static void Register(IRecurringJobManager recurringJobs, BlobReclamationOptions options)
    {
        if (!options.Enabled)
        {
            recurringJobs.RemoveIfExists(JobId);
            return;
        }

        var jobOptions = new RecurringJobOptions
        {
            MisfireHandling = MisfireHandlingMode.Ignorable
        };

        recurringJobs.AddOrUpdate<BlobReclamationJob>(JobId, x => x.SweepAsync(CancellationToken.None), options.Cron, jobOptions);
    }


    // Not retried: a sweep that fails is a sweep that deleted less than it could have, which is the
    // harmless direction, and the next day's picks up where it left off.
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 0)]
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        // Checked here as well as at registration: removing the recurring job leaves one already
        // queued, or triggered by hand from the dashboard, to run.
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Blob reclamation is disabled; skipping the sweep.");
            return;
        }

        DateTimeOffset cutoff = timeService.Now() - options.Value.MinimumBlobAge;

        // Every container is planned before any is touched, so a plan that fails the check below stops
        // the whole sweep rather than the containers after it. Each plan still lists its blobs before it
        // reads its registrations.
        ContainerSweep[] sweeps =
        [
            await PlanModsAsync(cutoff, cancellationToken),
            await PlanImagesAsync(cutoff, cancellationToken),
            await PlanSavegamesAsync(cutoff, cancellationToken)
        ];

        var implausible = sweeps
            .Where(x => x.Plan.IsImplausible(options.Value.MaxReclaimableShare))
            .ToList();

        if (implausible.Count > 0)
        {
            foreach (var sweep in implausible)
            {
                logger.LogError(
                    "Blob reclamation would reclaim {Reclaimable} of {Scanned} blobs in '{Container}', more than BlobReclamation:MaxReclaimableShare ({MaxShare:P0}) allows.",
                    sweep.Plan.Reclaimable.Count,
                    sweep.Plan.Scanned,
                    sweep.Container,
                    options.Value.MaxReclaimableShare);
            }

            // Failed rather than returned from, so the refusal shows in the dashboard as well as the log.
            throw new InvalidOperationException(
                "Blob reclamation refused to run and deleted nothing: a container would lose more of its blobs than " +
                "BlobReclamation:MaxReclaimableShare allows. That usually means the database is not the one the storage " +
                "account's blobs are registered in. If it is, and that much really is garbage, raise the share for one run.");
        }

        foreach (var sweep in sweeps)
        {
            await ApplyAsync(sweep, cancellationToken);
        }
    }

    private async Task<ContainerSweep> PlanModsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var stored = new List<StoredBlob>();
        await foreach (var blob in modStorage.ListStoredMods(cancellationToken))
        {
            stored.Add(blob);
        }

        var registered = (await dbContext.ModVersions
            .AsNoTracking()
            .Select(x => new { x.RepoId, x.ModId, x.Id })
            .ToListAsync(cancellationToken))
            .Select(x => new ModBlobAddress(x.RepoId, x.ModId, x.Id))
            .ToHashSet();

        return new ContainerSweep("mods", BlobReclamation.PlanModSweep(stored, registered, cutoff), modStorage.DeleteStoredBlob);
    }

    private async Task<ContainerSweep> PlanImagesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var stored = new List<StoredBlob>();
        await foreach (var blob in imageStorage.ListStoredImages(cancellationToken))
        {
            stored.Add(blob);
        }

        // Deduplicated in the database rather than here: one image is referenced by every version of
        // every mod that ships it, which is the whole point of addressing them by content.
        var referenced = (await dbContext.ModVersions
            .AsNoTracking()
            .SelectMany(x => x.Images)
            .Select(x => x.Hash)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        return new ContainerSweep("mod-images", BlobReclamation.PlanImageSweep(stored, referenced, cutoff), imageStorage.DeleteStoredBlob);
    }

    private async Task<ContainerSweep> PlanSavegamesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var stored = new List<StoredBlob>();
        await foreach (var blob in savegameStorage.ListStoredSavegames(cancellationToken))
        {
            stored.Add(blob);
        }

        // A set of addresses rather than one entry per snapshot, because several snapshots can name
        // one address: a restore copies an old snapshot forward under the same hash, and so does a
        // night that changed nothing. So the question worth asking of a blob is whether anything
        // still refers to it, never how many snapshots do or which one owns it.
        var registered = await dbContext.SavegameSnapshots.GetRegisteredBlobAddressesAsync(cancellationToken);

        return new ContainerSweep("savegames", BlobReclamation.PlanSavegameSweep(stored, registered, cutoff), savegameStorage.DeleteStoredBlob);
    }

    private async Task ApplyAsync(ContainerSweep sweep, CancellationToken cancellationToken)
    {
        var (container, plan, delete) = sweep;

        foreach (var blob in plan.Reclaimable)
        {
            await delete(blob.Name, cancellationToken);
        }

        logger.LogInformation(
            "Reclaimed {Reclaimed} of {Scanned} blobs in '{Container}'; {Retained} unreferenced but too recent, {Unrecognised} unrecognised.",
            plan.Reclaimable.Count,
            plan.Scanned,
            container,
            plan.Retained.Count,
            plan.Unrecognised.Count);

        foreach (var name in plan.Unrecognised)
        {
            // Warned individually rather than counted away: a name this sweep cannot parse means
            // either something else is writing into the container or the layout has moved, and both
            // are things somebody has to look at.
            logger.LogWarning("Blob '{Name}' in '{Container}' does not match the expected layout and was left alone.", name, container);
        }
    }


    private sealed record ContainerSweep(string Container, ReclamationPlan Plan, Func<string, CancellationToken, Task> Delete);
}
