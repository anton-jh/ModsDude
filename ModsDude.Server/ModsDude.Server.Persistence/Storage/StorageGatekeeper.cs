using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Application.Services;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.DbContexts;

namespace ModsDude.Server.Persistence.Storage;

/// <summary>
/// Reads the binding and the markers, carries out what <see cref="StorageGate"/> decides, advances
/// the generation, and binds by hand. See <see cref="StorageBinding"/>.
/// </summary>
public class StorageGatekeeper(
    ApplicationDbContext dbContext,
    IBlobContainers blobs,
    ITimeService timeService,
    ILogger<StorageGatekeeper> logger)
    : IStorageGatekeeper
{
    public async Task<StorageInspection> InspectAsync(int minimumSoftDeleteDays, CancellationToken cancellationToken)
    {
        var binding = await dbContext.StorageBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        var containers = new List<InspectedContainer>();
        foreach (var container in StorageContainerNames.All)
        {
            var marker = await blobs.ReadMarkerAsync(container, cancellationToken);
            var isEmpty = await blobs.IsEmptyAsync(container, cancellationToken);

            containers.Add(new InspectedContainer(new ContainerState(container, marker.Marker, isEmpty, marker.IsUnreadable), marker.Version));
        }

        var softDeleteDays = await blobs.GetSoftDeleteDaysAsync(cancellationToken);

        var decision = StorageGate.Decide(new StorageGateInput(
            binding,
            [.. containers.Select(x => x.State)],
            blobs.StorageAccount,
            softDeleteDays,
            minimumSoftDeleteDays));

        return new StorageInspection(binding, blobs.StorageAccount, containers, softDeleteDays, decision);
    }

    public async Task<IReadOnlyList<string>> PrepareAsync(StorageInspection inspection, CancellationToken cancellationToken)
    {
        var decision = inspection.Decision;

        if (!decision.MayWrite)
        {
            throw new InvalidOperationException("Storage the gate refused is never written to.");
        }

        if (!decision.CreateBinding && !decision.RecordStorageAccount && decision.ToMark.Count == 0 && decision.ToUpgrade.Count == 0)
        {
            return [];
        }

        var binding = await dbContext.StorageBindings.SingleOrDefaultAsync(cancellationToken);

        if (decision.CreateBinding)
        {
            if (binding is not null)
            {
                return ["Another run bound storage while this one inspected it."];
            }

            binding = StorageBinding.Create(Guid.NewGuid(), timeService.Now(), inspection.StorageAccount);
            dbContext.StorageBindings.Add(binding);
        }
        else if (binding is null || binding.BindingId != inspection.Binding?.BindingId)
        {
            return ["The binding changed while this run inspected storage."];
        }

        if (decision.RecordStorageAccount)
        {
            binding.RecordStorageAccount(inspection.StorageAccount);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "The storage binding changed while this run prepared it.");
            return ["The binding changed while this run inspected storage."];
        }

        if (decision.CreateBinding)
        {
            logger.LogInformation("Bound this database to empty storage as {BindingId}.", binding.BindingId);
        }

        var refusals = new List<string>();

        foreach (var container in decision.ToMark)
        {
            if (await blobs.WriteMarkerAsync(container, binding.Marker, null, cancellationToken))
            {
                logger.LogInformation("Marked empty container {Container} as bound to {BindingId}.", StorageContainerNames.Of(container), binding.BindingId);
            }
            else
            {
                refusals.Add($"'{StorageContainerNames.Of(container)}' was marked by somebody else while this run marked it.");
            }
        }

        foreach (var container in decision.ToUpgrade)
        {
            if (await blobs.WriteMarkerAsync(container, binding.Marker, inspection.VersionOf(container), cancellationToken))
            {
                logger.LogInformation("Upgraded the marker of container {Container} to generation {Generation}.", StorageContainerNames.Of(container), binding.Marker.Generation);
            }
            else
            {
                refusals.Add($"The marker of '{StorageContainerNames.Of(container)}' changed while this run upgraded it.");
            }
        }

        return refusals;
    }

    public async Task AdvanceAsync(CancellationToken cancellationToken)
    {
        var binding = await dbContext.StorageBindings.SingleAsync(cancellationToken);

        binding.BeginAdvance(Guid.NewGuid());
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var container in StorageContainerNames.All)
        {
            var marker = await blobs.ReadMarkerAsync(container, cancellationToken);

            if (binding.Judge(marker.Marker) is not ContainerBinding.Bound
                || !await blobs.WriteMarkerAsync(container, binding.Marker, marker.Version, cancellationToken))
            {
                throw new InvalidOperationException($"The marker of '{StorageContainerNames.Of(container)}' changed during the run.");
            }
        }

        binding.CompleteAdvance();
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Advanced the storage binding to generation {Generation}.", binding.Generation);
    }

    public async Task<IReadOnlyList<string>> BindByHandAsync(string operatorName, CancellationToken cancellationToken)
    {
        var markers = new Dictionary<StorageContainer, MarkerRead>();
        foreach (var container in StorageContainerNames.All)
        {
            markers[container] = await blobs.ReadMarkerAsync(container, cancellationToken);
        }

        var highest = markers.Values.Max(x => x.Marker?.Generation);
        var binding = await dbContext.StorageBindings.SingleOrDefaultAsync(cancellationToken);

        if (binding is null)
        {
            binding = StorageBinding.Create(Guid.NewGuid(), timeService.Now(), blobs.StorageAccount);
            dbContext.StorageBindings.Add(binding);
        }

        binding.Rebind(blobs.StorageAccount, highest, Guid.NewGuid());

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "The storage binding changed while {Operator} bound storage by hand.", operatorName);
            return ["The binding changed while binding. Try again."];
        }

        var failures = new List<string>();

        foreach (var (container, marker) in markers.OrderBy(x => x.Key))
        {
            if (!await blobs.WriteMarkerAsync(container, binding.Marker, marker.Version, cancellationToken))
            {
                failures.Add($"The marker of '{StorageContainerNames.Of(container)}' changed while binding. Try again.");
            }
        }

        logger.LogWarning(
            "Admin {Operator} bound storage account {Account} to this database as {BindingId} at generation {Generation}; {Failed} markers could not be written.",
            operatorName, blobs.StorageAccount, binding.BindingId, binding.Generation, failures.Count);

        return failures;
    }
}


public sealed record InspectedContainer(ContainerState State, string? MarkerVersion);

public sealed record StorageInspection(
    StorageBinding? Binding,
    string StorageAccount,
    IReadOnlyList<InspectedContainer> Containers,
    int? SoftDeleteDays,
    StorageGateDecision Decision)
{
    public string? VersionOf(StorageContainer container) => Containers.Single(x => x.State.Container == container).MarkerVersion;
}
