using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.Shared.Converters;

/// <summary>
/// The sideways nudge that centres an icon glyph's ink rather than its box, for a glyph drawn centred
/// in a frame. Some icon font glyphs sit off-centre in their own box - Segoe Fluent Icons' plus has more
/// room on its right than its left - which reads as off-centre in a tile.
/// </summary>
/// <remarks>
/// Whole pixels only, so a thin stroke stays on one pixel column instead of blurring across two.
/// Takes the TextBlock's text, font family and font size.
/// </remarks>
public sealed class GlyphCenteringConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [string { Length: 1 } text, FontFamily family, double size]
            || new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var glyphs) is false
            || glyphs.CharacterToGlyphMap.TryGetValue(text[0], out var glyph) is false)
        {
            return Transform.Identity;
        }

        var offset = (glyphs.RightSideBearings[glyph] - glyphs.LeftSideBearings[glyph]) / 2 * size;

        return new TranslateTransform(Math.Round(offset, MidpointRounding.AwayFromZero), 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
