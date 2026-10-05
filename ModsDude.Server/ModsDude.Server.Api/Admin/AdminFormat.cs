using ModsDude.Server.Domain.Backups;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;
using System.Globalization;

namespace ModsDude.Server.Api.Admin;

public static class AdminFormat
{
    private static readonly string[] _byteUnits = ["B", "KiB", "MiB", "GiB", "TiB"];


    public static string Time(DateTime time)
    {
        return time.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    public static string Age(TimeSpan age)
    {
        if (age < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(0, (int)age.TotalMinutes)} min";
        }

        if (age < TimeSpan.FromDays(2))
        {
            return $"{(int)age.TotalHours} h";
        }

        return $"{(int)age.TotalDays} days";
    }

    public static string Freshness(BackupFreshness freshness) => freshness switch
    {
        BackupFreshness.Ok => "OK",
        BackupFreshness.Late => "Late",
        BackupFreshness.Missing => "No backups",
        _ => throw new ArgumentOutOfRangeException(nameof(freshness), freshness, null)
    };

    public static string Date(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string Count(long count)
    {
        return count.ToString("N0", CultureInfo.InvariantCulture);
    }

    public static string Bytes(long bytes)
    {
        double size = Math.Abs(bytes);
        var unit = 0;

        while (size >= 1024 && unit < _byteUnits.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var sign = bytes < 0 ? "-" : "";
        var format = unit == 0 ? "0" : "0.0";

        return $"{sign}{size.ToString(format, CultureInfo.InvariantCulture)} {_byteUnits[unit]}";
    }

    public static string SignedBytes(long bytes)
    {
        return bytes > 0 ? $"+{Bytes(bytes)}" : Bytes(bytes);
    }

    public static string User(UserId userId, DisplayName displayName)
    {
        return $"{displayName.Value} #{UserTag.For(userId)}";
    }

    public static string Repo(RepoId repoId, RepoName name)
    {
        return $"{name.Value} #{RepoTag.For(repoId)}";
    }
}
