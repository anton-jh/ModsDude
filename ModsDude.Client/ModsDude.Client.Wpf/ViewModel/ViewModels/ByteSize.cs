using System.Globalization;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// A byte count in the units somebody thinks in. One implementation, because a savegame and a
/// content store disagreeing about what a megabyte rounds to would be a bug nobody would ever chase.
/// </summary>
public static class ByteSize
{
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
