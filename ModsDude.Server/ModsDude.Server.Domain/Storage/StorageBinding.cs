namespace ModsDude.Server.Domain.Storage;

/// <summary>
/// Which storage account this database belongs to. The same id is written into each blob container as
/// a marker, so the reclamation sweep can tell a container measured against its own database from one
/// measured against another - a reset dev database, a restore, a connection string pointing elsewhere -
/// to which every blob looks orphaned.
/// </summary>
/// <remarks>
/// One row. Created by the first sweep that finds neither it nor any marker.
/// </remarks>
public class StorageBinding(Guid bindingId, DateTime created)
{
    public const int SingletonId = 1;

    /// <summary>What the marker blob is called in every container. No sweep layout parses it.</summary>
    public const string MarkerBlobName = "modsdude-storage-binding";


    public int Id { get; private set; } = SingletonId;
    public Guid BindingId { get; private set; } = bindingId;
    public DateTime Created { get; private set; } = created;


    /// <summary>How a container relates to the database, from the binding and the container's marker.</summary>
    public static ContainerBinding Judge(Guid? databaseBinding, Guid? containerMarker)
    {
        return (databaseBinding, containerMarker) switch
        {
            (_, null) => ContainerBinding.Unbound,
            (Guid database, Guid marker) when database == marker => ContainerBinding.Bound,
            _ => ContainerBinding.Foreign
        };
    }
}


public enum ContainerBinding
{
    /// <summary>The container carries this database's marker: the registrations are its own.</summary>
    Bound,

    /// <summary>
    /// No marker yet: a container from before binding existed, or one created since. Swept only past
    /// the plausibility check, and marked afterwards.
    /// </summary>
    Unbound,

    /// <summary>The container carries another database's marker, or this database has none to match it: never swept.</summary>
    Foreign
}
