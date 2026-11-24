namespace ModsDude.Server.Domain.Storage;

/// <param name="Marker">The container's marker, or null where it has none or it can't be read.</param>
/// <param name="IsEmpty">Whether the container holds no blob besides its marker.</param>
/// <param name="MarkerIsUnreadable">Whether the container has a marker blob that holds something other than a marker.</param>
public sealed record ContainerState(StorageContainer Container, ContainerMarker? Marker, bool IsEmpty, bool MarkerIsUnreadable = false);

/// <param name="SoftDeleteDays">How long storage keeps deleted blobs: null where it cannot be read, 0 where soft delete is off.</param>
public sealed record StorageGateInput(
    StorageBinding? Binding,
    IReadOnlyList<ContainerState> Containers,
    string StorageAccount,
    int? SoftDeleteDays,
    int MinimumSoftDeleteDays);

/// <param name="WriteRefusals">Why nothing may be written to storage or deleted from it: the storage is not this database's.</param>
/// <param name="DeleteRefusals">Why nothing may be deleted, though the storage is this database's.</param>
/// <param name="CreateBinding">Whether the database gets its first binding, against storage that is entirely empty.</param>
/// <param name="ToMark">Empty containers with no marker, to be marked with the binding's <see cref="StorageBinding.Marker"/>.</param>
/// <param name="ToUpgrade">Containers whose marker predates generations, to be rewritten with the binding's <see cref="StorageBinding.Marker"/>.</param>
/// <param name="RecordStorageAccount">Whether the binding has no storage account recorded yet and gets this one.</param>
public sealed record StorageGateDecision(
    IReadOnlyList<string> WriteRefusals,
    IReadOnlyList<string> DeleteRefusals,
    bool CreateBinding,
    IReadOnlyList<StorageContainer> ToMark,
    IReadOnlyList<StorageContainer> ToUpgrade,
    bool RecordStorageAccount)
{
    public bool MayWrite => WriteRefusals.Count == 0;
    public bool MayDelete => MayWrite && DeleteRefusals.Count == 0;
}

/// <summary>
/// Whether this database may write to storage, and whether it may delete from it. Pure, because it is
/// what stands between a database that is not storage's own and every blob in the account.
/// </summary>
/// <remarks>
/// Strict on purpose. A container is bound only when it is empty, or by hand on the admin page. A
/// marker that does not match refuses, and so does soft delete that cannot be confirmed: every delete
/// must be recoverable from storage itself.
/// </remarks>
public static class StorageGate
{
    public static StorageGateDecision Decide(StorageGateInput input)
    {
        var deleteRefusals = SoftDeleteRefusals(input.SoftDeleteDays, input.MinimumSoftDeleteDays);

        var unreadable = input.Containers
            .Where(x => x.MarkerIsUnreadable)
            .OrderBy(x => x.Container)
            .Select(x => $"'{StorageContainerNames.Of(x.Container)}' has a marker that can't be read.")
            .ToList();

        if (unreadable.Count > 0)
        {
            return Refused(unreadable, deleteRefusals);
        }

        return input.Binding is StorageBinding binding
            ? DecideBound(input, binding, deleteRefusals)
            : DecideUnbound(input, deleteRefusals);
    }


    private static StorageGateDecision DecideUnbound(StorageGateInput input, IReadOnlyList<string> deleteRefusals)
    {
        if (input.Containers.All(x => x.Marker is null && x.IsEmpty))
        {
            return new StorageGateDecision([], deleteRefusals, true, [.. input.Containers.Select(x => x.Container).Order()], [], false);
        }

        var refusals = input.Containers
            .Where(x => x.Marker is not null)
            .OrderBy(x => x.Container)
            .Select(x => $"'{StorageContainerNames.Of(x.Container)}' is bound to a database, and this one has no binding.")
            .ToList();

        if (refusals.Count == 0)
        {
            refusals.Add("This database has no binding, and storage is not empty.");
        }

        return Refused(refusals, deleteRefusals);
    }

    private static StorageGateDecision DecideBound(StorageGateInput input, StorageBinding binding, IReadOnlyList<string> deleteRefusals)
    {
        var refusals = new List<string>();
        var toMark = new List<StorageContainer>();
        var toUpgrade = new List<StorageContainer>();

        if (binding.StorageAccount is string bound && !string.Equals(bound, input.StorageAccount, StringComparison.OrdinalIgnoreCase))
        {
            refusals.Add($"This database is bound to storage account '{bound}', not '{input.StorageAccount}'.");
        }

        foreach (var container in input.Containers.OrderBy(x => x.Container))
        {
            switch (binding.Judge(container.Marker))
            {
                case ContainerBinding.Bound:
                    break;

                case ContainerBinding.Unbound when container.IsEmpty:
                    toMark.Add(container.Container);
                    break;

                case ContainerBinding.Unbound:
                    refusals.Add($"'{StorageContainerNames.Of(container.Container)}' has no marker and is not empty.");
                    break;

                case ContainerBinding.Legacy:
                    toUpgrade.Add(container.Container);
                    break;

                case ContainerBinding.Stale:
                    refusals.Add($"'{StorageContainerNames.Of(container.Container)}' is at generation {container.Marker!.Generation?.ToString() ?? "none"}, and this database is at {binding.Generation}.");
                    break;

                case ContainerBinding.Foreign:
                    refusals.Add($"'{StorageContainerNames.Of(container.Container)}' is bound to another database.");
                    break;
            }
        }

        return refusals.Count > 0
            ? Refused(refusals, deleteRefusals)
            : new StorageGateDecision([], deleteRefusals, false, toMark, toUpgrade, binding.StorageAccount is null);
    }

    private static IReadOnlyList<string> SoftDeleteRefusals(int? softDeleteDays, int minimumDays)
    {
        return softDeleteDays switch
        {
            null => ["The soft delete setting of the storage account can't be read."],
            0 => ["Soft delete is off on the storage account."],
            int days when days < minimumDays => [$"Soft delete keeps deleted blobs for {days} days, fewer than {minimumDays}."],
            _ => []
        };
    }

    private static StorageGateDecision Refused(IReadOnlyList<string> writeRefusals, IReadOnlyList<string> deleteRefusals)
    {
        return new StorageGateDecision(writeRefusals, deleteRefusals, false, [], [], false);
    }
}
