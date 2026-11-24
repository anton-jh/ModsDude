namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// Which storage this database belongs to. Every container carries a marker naming the binding and a
/// generation; nothing is written to or deleted from storage unless every marker matches.
/// </summary>
/// <remarks>
/// <para>
/// The binding id tells this database from another one: a reset database, or a connection string
/// pointing elsewhere.
/// </para>
/// <para>
/// The generation tells this database from a copy of itself. Every deleting run advances it, in the
/// database and in every marker, so a database restored from a backup, or a second database sharing the
/// storage, is behind the markers and refuses. Advancing is two-phase: <see cref="PendingGeneration"/>
/// is committed first, then the markers are written, then <see cref="CompleteAdvance"/>. A marker at
/// either generation is accepted, so a run that died between the phases is finished by the next one.
/// </para>
/// <para>
/// Every advance has its own id, written into the markers with the generation. Two copies of the database
/// advancing at once both name the same next generation, and only the id tells their markers apart, so
/// neither accepts the other's.
/// </para>
/// </remarks>
public class StorageBinding
{
    public const int SingletonId = 1;

    /// <summary>What the marker blob is called in every container. No blob name layout parses it.</summary>
    public const string MarkerBlobName = "modsdude-storage-binding";


    private StorageBinding(Guid bindingId, DateTime created, string storageAccount)
    {
        BindingId = bindingId;
        Created = created;
        StorageAccount = storageAccount;
    }

    public static StorageBinding Create(Guid bindingId, DateTime created, string storageAccount)
    {
        return new StorageBinding(bindingId, created, storageAccount);
    }


    public int Id { get; private set; } = SingletonId;
    public Guid BindingId { get; private set; }
    public DateTime Created { get; private set; }

    /// <summary>The host of the storage account the binding was made against. Null only on a binding from before it was recorded.</summary>
    public string? StorageAccount { get; private set; }

    public long Generation { get; private set; }

    /// <summary>The advance or rebind that reached <see cref="Generation"/>. Null until the first one.</summary>
    public Guid? AdvanceId { get; private set; }

    public long? PendingGeneration { get; private set; }
    public Guid? PendingAdvanceId { get; private set; }

    /// <summary>What every marker is written as now.</summary>
    public ContainerMarker Marker => PendingGeneration is long pending
        ? new(BindingId, pending, PendingAdvanceId)
        : new(BindingId, Generation, AdvanceId);


    public ContainerBinding Judge(ContainerMarker? marker)
    {
        if (marker is null)
        {
            return ContainerBinding.Unbound;
        }

        if (marker.BindingId != BindingId)
        {
            return ContainerBinding.Foreign;
        }

        if (marker.Generation is not long generation)
        {
            return Generation == 0 && PendingGeneration is null ? ContainerBinding.Legacy : ContainerBinding.Stale;
        }

        var isCurrent = generation == Generation && marker.AdvanceId == AdvanceId;
        var isPending = generation == PendingGeneration && marker.AdvanceId == PendingAdvanceId;

        return isCurrent || isPending ? ContainerBinding.Bound : ContainerBinding.Stale;
    }

    /// <summary>
    /// The first phase of advancing. Repeating it before <see cref="CompleteAdvance"/> keeps the advance
    /// already begun, which is what lets a rerun finish a run that died between the phases.
    /// </summary>
    /// <param name="advanceId">A new id, used only when no advance has begun.</param>
    public void BeginAdvance(Guid advanceId)
    {
        if (PendingGeneration is not null)
        {
            return;
        }

        PendingGeneration = Generation + 1;
        PendingAdvanceId = advanceId;
    }

    public void CompleteAdvance()
    {
        Generation = PendingGeneration ?? throw new InvalidOperationException("No advance has begun.");
        AdvanceId = PendingAdvanceId;
        PendingGeneration = null;
        PendingAdvanceId = null;
    }

    public void RecordStorageAccount(string storageAccount)
    {
        StorageAccount = storageAccount;
    }

    /// <summary>
    /// Takes the storage over by hand: a generation past any marker seen, so a database that wrote one
    /// of those markers is behind from now on. The caller then overwrites every marker with <see cref="Marker"/>.
    /// </summary>
    /// <param name="highestMarkerGeneration">The highest generation any container's marker holds, or null where none holds one.</param>
    /// <param name="advanceId">A new id for the rebind.</param>
    public void Rebind(string storageAccount, long? highestMarkerGeneration, Guid advanceId)
    {
        StorageAccount = storageAccount;
        Generation = Math.Max(Math.Max(Generation, PendingGeneration ?? 0), highestMarkerGeneration ?? 0) + 1;
        AdvanceId = advanceId;
        PendingGeneration = null;
        PendingAdvanceId = null;
    }
}


public enum ContainerBinding
{
    /// <summary>The marker names this binding at an accepted generation.</summary>
    Bound,

    /// <summary>No marker.</summary>
    Unbound,

    /// <summary>The marker names this binding and no generation, and this binding has never advanced: upgraded in place.</summary>
    Legacy,

    /// <summary>The marker names this binding at a generation this database is not at: a restored backup, or a second database.</summary>
    Stale,

    /// <summary>The marker names another binding.</summary>
    Foreign
}
