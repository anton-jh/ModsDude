using System.Globalization;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// A byte count in the units somebody thinks in. One implementation, because a savegame and a
/// content store disagreeing about what a megabyte rounds to would be a bug nobody would ever chase.
/// </summary>
public static class ByteSize
{
    /// <summary>
    /// Past this, a transfer is taken to outlast the strip's own promotion clock, so it is given a
    /// row from the start rather than three seconds into one.
    /// </summary>
    public const long LargeTransfer = 32L * 1024 * 1024;


    /// <summary>"41.2 MB / 68 MB", for a transfer that knows where it is going.</summary>
    public static string Describe(long transferred, long total)
    {
        return total > 0
            ? $"{Describe(transferred)} / {Describe(total)}"
            : Describe(transferred);
    }

    public static string Describe(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;

        foreach (var unit in new[] { "kB", "MB", "GB" })
        {
            value /= 1024;

            if (value < 1024)
            {
                return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {unit}");
            }
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} TB");
    }
}
