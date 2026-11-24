namespace ModsDude.Server.Persistence;

/// <summary>
/// The first key of every Postgres advisory lock the server takes, kept in one list so no two locks
/// share a space. The second key says which lock within the space.
/// </summary>
public static class AdvisoryLockSpaces
{
    /// <summary>One lock per day of storage statistics.</summary>
    public const int StorageStatistics = 1;

    /// <summary>The one storage maintenance run lock, at second key 0.</summary>
    public const int StorageMaintenanceRun = 2;

    /// <summary>One lock per blob, at the <c>hashtext</c> of its container and name.</summary>
    public const int Blob = 3;
}
