using ModsDude.Server.Domain.Backups;

namespace ModsDude.Server.Application.Dependencies;

/// <summary>Reads the off-site database backups that <c>deploy/backup.sh</c> uploads.</summary>
public interface IBackupStorageService
{
    Task<BackupListing> ListBackups(CancellationToken cancellationToken);
}


public abstract record BackupListing
{
    /// <summary>No backup storage account is configured, as in development.</summary>
    public sealed record NotConfigured : BackupListing;

    /// <summary>The backup container could not be listed. The cause is logged.</summary>
    public sealed record Unavailable : BackupListing;

    public sealed record Listed(IReadOnlyList<ListedBackupBlob> Blobs) : BackupListing;
}
