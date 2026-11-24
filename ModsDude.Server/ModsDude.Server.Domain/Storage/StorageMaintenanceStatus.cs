namespace ModsDude.Server.Domain.Storage;

/// <summary>How the last storage maintenance run went. One row.</summary>
public class StorageMaintenanceStatus
{
    public const int SingletonId = 1;


    public int Id { get; private set; } = SingletonId;
    public DateTime? LastRun { get; private set; }

    /// <summary>When the findings were last replaced.</summary>
    public DateTime? LastAudit { get; private set; }

    /// <summary>Why the last run wrote or deleted nothing. Empty when it was free to.</summary>
    public List<string> Refusals { get; private set; } = [];

    public int DeletedInLastRun { get; private set; }


    public void RecordRun(DateTime now, bool audited, IReadOnlyList<string> refusals, int deleted)
    {
        LastRun = now;

        if (audited)
        {
            LastAudit = now;
        }

        Refusals = [.. refusals];
        DeletedInLastRun = deleted;
    }
}
