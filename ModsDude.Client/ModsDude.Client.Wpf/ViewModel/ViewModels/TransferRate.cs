using ModsDude.Client.Core.Transfers;
using System.Globalization;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Speed limits in the unit the line is sold in. Megabits, not megabytes: "my connection is 100" is
/// a number in Mbit/s, and a limit field in any other unit is a field somebody fills in eight times
/// too high.
/// </summary>
public static class TransferRate
{
    private const double _bytesPerMegabit = 1_000_000 / 8d;

    /// <summary>
    /// The slowest limit the settings accept. Storage gives up on a request that moves too slowly,
    /// and below this a large block or range would start to get close to that.
    /// </summary>
    public const double MinimumMegabits = 1;


    public static long ToBytesPerSecond(double megabits) => (long)Math.Round(megabits * _bytesPerMegabit);

    public static double ToMegabits(long bytesPerSecond) => bytesPerSecond / _bytesPerMegabit;

    /// <summary>"50 Mbit/s".</summary>
    public static string Describe(long bytesPerSecond)
    {
        return string.Create(CultureInfo.CurrentCulture, $"{ToMegabits(bytesPerSecond):0.#} Mbit/s");
    }

    /// <summary>
    /// What limits hold back a task moving bytes in <paramref name="directions"/> - "Downloads capped
    /// at 50 Mbit/s" - or null when nothing does.
    /// </summary>
    public static string? DescribeLimits(TransferLimits limits, TransferDirection directions)
    {
        var down = directions.HasFlag(TransferDirection.Download) ? limits.Download.BytesPerSecond : null;
        var up = directions.HasFlag(TransferDirection.Upload) ? limits.Upload.BytesPerSecond : null;

        return (down, up) switch
        {
            (long d, long u) => $"Capped at {Describe(d)} down, {Describe(u)} up",
            (long d, null) => $"Downloads capped at {Describe(d)}",
            (null, long u) => $"Uploads capped at {Describe(u)}",
            _ => null
        };
    }
}
