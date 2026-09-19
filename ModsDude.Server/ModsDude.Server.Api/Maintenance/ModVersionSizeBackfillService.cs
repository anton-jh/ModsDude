using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Persistence.DbContexts;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;

namespace ModsDude.Server.Api.Maintenance;

/// <summary>
/// Fills in the size of every mod version that was registered before sizes were recorded, once, shortly
/// after the API starts.
/// </summary>
/// <remarks>
/// <para>
/// A size is a fact about the stored blob and storage already knows it, so this reads it from a single
/// container listing rather than asking about each version - a listing is one request per five thousand
/// blobs, and a per-version property read is one request each. A version whose blob is not in the
/// listing keeps no size and is tried again next start, which is the right answer for a registration
/// whose upload is somehow missing: nothing here can invent the bytes.
/// </para>
/// <para>
/// <b><c>Updated</c> is not touched.</b> The mod list's delta form is keyed on it, and restamping every
/// old version for a column that a full listing carries anyway would make the first delta after a
/// deploy the size of the whole list.
/// </para>
/// <para>
/// A no-op once there is nothing to fill, and it never takes the host down: a size that is missing is
/// shown as unknown, which is a smaller problem than an API that will not start.
/// </para>
/// </remarks>
public class ModVersionSizeBackfillService(
    IServiceScopeFactory scopeFactory,
    ILogger<ModVersionSizeBackfillService> logger)
    : BackgroundService
{
    /// <summary>Long enough to stay off the startup path, which is what the migrations are already using.</summary>
    private static readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(20);


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_startupDelay, stoppingToken);
            await BackfillAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Backfilling mod version sizes failed. They stay unknown until the next start.");
        }
    }


    private async Task BackfillAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IModStorageService>();

        var missing = await dbContext.ModVersions.GetAddressesWithoutSizeAsync(cancellationToken);

        if (missing.Count == 0)
        {
            return;
        }

        var sizes = new Dictionary<ModBlobAddress, long>();

        await foreach (var blob in storage.ListStoredMods(cancellationToken))
        {
            // A blob that reports no length is not one to record: no mod file is empty, and zero would
            // read as a size where unknown is the truth.
            if (blob.Length > 0 && BlobReclamation.TryParseModBlobName(blob.Name, out var address))
            {
                sizes[address] = blob.Length;
            }
        }

        var filled = 0;

        foreach (var address in missing)
        {
            if (sizes.TryGetValue(address, out var size) is false)
            {
                continue;
            }

            filled += await dbContext.ModVersions.RecordSizeAsync(address, size, cancellationToken);
        }

        logger.LogInformation(
            "Recorded the size of {Filled} of {Missing} mod versions that had none.", filled, missing.Count);
    }
}
