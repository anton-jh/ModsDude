namespace ModsDude.Server.Domain.Backups;

public enum BackupFreshness
{
    Ok,
    Late,
    Missing
}


/// <param name="Newest">The most recent backup of the tier, or <c>null</c> when it has none.</param>
/// <param name="Oldest">The oldest backup of the tier still kept, or <c>null</c> when it has none.</param>
/// <param name="TotalBytes">The size of every backup of the tier still kept.</param>
public record BackupTierStatus(BackupTier Tier, BackupFreshness Freshness, StoredBackup? Newest, StoredBackup? Oldest, int Count, long TotalBytes);


/// <param name="Unrecognised">Names that don't match the layout <c>deploy/backup.sh</c> writes, sorted.</param>
/// <param name="TotalBytes">The size of every blob in the container, unrecognised ones included.</param>
public record BackupOverview(IReadOnlyList<BackupTierStatus> Tiers, IReadOnlyList<string> Unrecognised, long TotalBytes);


public static class BackupStatus
{
    /// <summary>
    /// How old a tier's newest backup may be before the tier is late: its interval plus enough grace
    /// for one run that starts late or takes a while.
    /// </summary>
    private static TimeSpan AllowedAge(BackupTier tier) => tier switch
    {
        BackupTier.Hourly => TimeSpan.FromHours(2),
        BackupTier.Daily => TimeSpan.FromHours(26),
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null)
    };

    public static BackupOverview Summarise(IEnumerable<ListedBackupBlob> listed, DateTimeOffset now)
    {
        var backups = new List<StoredBackup>();
        var unrecognised = new List<string>();
        var totalBytes = 0L;

        foreach (var blob in listed)
        {
            totalBytes += blob.Length;

            if (StoredBackup.TryParse(blob, out var backup))
            {
                backups.Add(backup);
            }
            else
            {
                unrecognised.Add(blob.Name);
            }
        }

        var tiers = Enum.GetValues<BackupTier>()
            .Select(tier => SummariseTier(tier, backups.Where(x => x.Tier == tier).ToList(), now))
            .ToList();

        return new BackupOverview(tiers, [.. unrecognised.Order(StringComparer.Ordinal)], totalBytes);
    }


    private static BackupTierStatus SummariseTier(BackupTier tier, IReadOnlyList<StoredBackup> backups, DateTimeOffset now)
    {
        if (backups.Count == 0)
        {
            return new BackupTierStatus(tier, BackupFreshness.Missing, null, null, 0, 0);
        }

        var ordered = backups
            .OrderBy(x => x.TakenAt)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        var newest = ordered[^1];
        var freshness = now - newest.TakenAt <= AllowedAge(tier)
            ? BackupFreshness.Ok
            : BackupFreshness.Late;

        return new BackupTierStatus(tier, freshness, newest, ordered[0], ordered.Count, ordered.Sum(x => x.Length));
    }
}
