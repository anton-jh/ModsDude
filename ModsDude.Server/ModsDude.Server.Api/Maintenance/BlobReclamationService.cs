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
/// </remarks>
public class BlobReclamationService(
    IServiceScopeFactory scopeFactory,
    ITimeService timeService,
    IOptions<BlobReclamationOptions> options,
    ILogger<BlobReclamationService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Blob reclamation is disabled; no sweep will run.");
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval);

        // WaitForNextTickAsync waits the period out before its first tick, which is what keeps the
        // sweep off the startup path.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A sweep that fails is a sweep that deleted less than it could have, which is the
                // harmless direction. Never let it take the host down.
                logger.LogError(exception, "Blob reclamation sweep failed.");
            }
        }
    }


    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var modStorage = scope.ServiceProvider.GetRequiredService<IModStorageService>();
        var imageStorage = scope.ServiceProvider.GetRequiredService<IModImageStorageService>();
        var savegameStorage = scope.ServiceProvider.GetRequiredService<ISavegameStorageService>();

        DateTimeOffset cutoff = timeService.Now() - options.Value.MinimumBlobAge;

        await SweepModsAsync(dbContext, modStorage, cutoff, cancellationToken);
        await SweepImagesAsync(dbContext, imageStorage, cutoff, cancellationToken);
        await SweepSavegamesAsync(dbContext, savegameStorage, cutoff, cancellationToken);
    }

    private async Task SweepModsAsync(
        ApplicationDbContext dbContext,
        IModStorageService storage,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var stored = new List<StoredBlob>();
        await foreach (var blob in storage.ListStoredMods(cancellationToken))
        {
            stored.Add(blob);
        }

        var registered = (await dbContext.ModVersions
            .AsNoTracking()
            .Select(x => new { x.RepoId, x.ModId, x.Id })
            .ToListAsync(cancellationToken))
            .Select(x => new ModBlobAddress(x.RepoId, x.ModId, x.Id))
            .ToHashSet();

        var plan = BlobReclamation.PlanModSweep(stored, registered, cutoff);

        await ApplyAsync("mods", plan, stored.Count, storage.DeleteStoredBlob, cancellationToken);
    }

    private async Task SweepImagesAsync(
        ApplicationDbContext dbContext,
        IModImageStorageService storage,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var stored = new List<StoredBlob>();
        await foreach (var blob in storage.ListStoredImages(cancellationToken))
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

        var plan = BlobReclamation.PlanImageSweep(stored, referenced, cutoff);

        await ApplyAsync("mod-images", plan, stored.Count, storage.DeleteStoredBlob, cancellationToken);
    }

    private async Task SweepSavegamesAsync(
        ApplicationDbContext dbContext,
        ISavegameStorageService storage,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var stored = new List<StoredBlob>();
        await foreach (var blob in storage.ListStoredSavegames(cancellationToken))
        {
            stored.Add(blob);
        }

        // A set of addresses rather than one entry per version, because several versions can name
        // one address: a restore copies an old version forward under the same hash, and so does a
        // night that changed nothing. So the question worth asking of a blob is whether anything
        // still refers to it, never how many versions do or which one owns it.
        var registered = await dbContext.SavegameVersions.GetRegisteredBlobAddressesAsync(cancellationToken);

        var plan = BlobReclamation.PlanSavegameSweep(stored, registered, cutoff);

        await ApplyAsync("savegames", plan, stored.Count, storage.DeleteStoredBlob, cancellationToken);
    }

    private async Task ApplyAsync(
        string container,
        ReclamationPlan plan,
        int scanned,
        Func<string, CancellationToken, Task> delete,
        CancellationToken cancellationToken)
    {
        foreach (var blob in plan.Reclaimable)
        {
            await delete(blob.Name, cancellationToken);
        }

        logger.LogInformation(
            "Reclaimed {Reclaimed} of {Scanned} blobs in '{Container}'; {Retained} unreferenced but too recent, {Unrecognised} unrecognised.",
            plan.Reclaimable.Count,
            scanned,
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
}
