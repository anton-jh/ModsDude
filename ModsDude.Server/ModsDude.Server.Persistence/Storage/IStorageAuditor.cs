namespace ModsDude.Server.Persistence.Storage;

public interface IStorageAuditor
{
    /// <param name="mayLabel">Whether storage is this database's, so referenced blobs may be labelled.</param>
    Task<StorageAuditOutcome> AuditAsync(bool mayLabel, CancellationToken cancellationToken);
}
