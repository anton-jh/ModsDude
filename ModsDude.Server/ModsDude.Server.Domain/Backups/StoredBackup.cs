using System.Globalization;

namespace ModsDude.Server.Domain.Backups;

public enum BackupTier
{
    Hourly,
    Daily
}


/// <summary>A blob in the backup container as storage lists it, before its name is understood.</summary>
public readonly record struct ListedBackupBlob(string Name, long Length);


/// <param name="TakenAt">When the dump started, which is the moment it holds the database as of.</param>
public readonly record struct StoredBackup(BackupTier Tier, string Name, DateTimeOffset TakenAt, long Length)
{
    private const string _timestampFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const string _fileNamePrefix = "modsdude-";
    private const string _fileNameSuffix = ".dump";


    /// <summary>
    /// Parses the layout <c>deploy/backup.sh</c> writes: <c>{tier}/modsdude-{yyyyMMddTHHmmssZ}.dump</c>.
    /// </summary>
    public static bool TryParse(ListedBackupBlob blob, out StoredBackup backup)
    {
        backup = default;

        var segments = blob.Name.Split('/');

        if (segments.Length != 2
            || !TryParseTier(segments[0], out var tier)
            || !segments[1].StartsWith(_fileNamePrefix, StringComparison.Ordinal)
            || !segments[1].EndsWith(_fileNameSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var timestamp = segments[1][_fileNamePrefix.Length..^_fileNameSuffix.Length];

        if (!DateTimeOffset.TryParseExact(
            timestamp,
            _timestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var takenAt))
        {
            return false;
        }

        backup = new StoredBackup(tier, blob.Name, takenAt, blob.Length);

        return true;
    }


    private static bool TryParseTier(string segment, out BackupTier tier)
    {
        switch (segment)
        {
            case "hourly":
                tier = BackupTier.Hourly;
                return true;

            case "daily":
                tier = BackupTier.Daily;
                return true;

            default:
                tier = default;
                return false;
        }
    }
}
