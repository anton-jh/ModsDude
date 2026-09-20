using System.Globalization;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.View.ValueConverters;

/// <summary>
/// What is left of a row's width once the things beside an element have taken theirs, for an element that
/// should give way to them - a maximum width for a trimmed name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a converter:</b> in a grid, an Auto column is measured against unlimited width and is never
/// squeezed, and a row of text blocks in a panel is the same. So "trim this one before that one" is not
/// something the layout can be asked for; it has to be handed to the one that should give way as a
/// number.
/// </para>
/// <para>
/// Bound as <c>[row width, then the widths of everything that must keep theirs]</c>, with a parameter of
/// <c>minimum;reserve;cap;unknown</c>, each optional and in that order. <c>minimum</c> is the least the
/// element is ever given, so a very narrow row trims it to a stub and not to nothing (default 0);
/// <c>reserve</c> is width set aside for something that is not measurable, or that the element must always
/// leave room for (default 0); <c>cap</c> is the most it is ever given, however much room there is (default
/// none). Until the row has a width there is nothing to share: the answer is then no limit at all, or, where
/// the fourth part is <c>auto</c>, NaN - which is what an explicit <c>Width</c> needs, since it cannot be
/// infinite.
/// </para>
/// </remarks>
public sealed class ShareWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split(';') ?? [];

        if (values.Length == 0 || values[0] is not double total || double.IsNaN(total) || total <= 0)
        {
            return parts.Length > 3 && parts[3] == "auto" ? double.NaN : double.PositiveInfinity;
        }

        var others = values.Skip(1).OfType<double>().Where(x => double.IsNaN(x) is false).Sum();

        var minimum = parts.Length > 0 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var m) ? m : 0;
        var reserve = parts.Length > 1 && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var r) ? r : 0;

        var cap = parts.Length > 2 && double.TryParse(parts[2], CultureInfo.InvariantCulture, out var c) ? c : double.PositiveInfinity;

        return Math.Min(cap, Math.Max(minimum, total - others - reserve));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
