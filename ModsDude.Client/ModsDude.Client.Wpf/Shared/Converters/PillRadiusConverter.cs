using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.Shared.Converters;

/// <summary>
/// The corner radius that makes a stadium of a shape: half its height, bound from its ActualHeight. A
/// radius larger than that is scaled down by WPF against the width as well as the height, which turns a
/// wide pill into an ellipse. Gives a CornerRadius for a Border or a double for a Rectangle's RadiusX/Y.
/// </summary>
public sealed class PillRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var radius = value is double height && double.IsFinite(height) ? height / 2 : 0;
        return targetType == typeof(CornerRadius) ? new CornerRadius(radius) : radius;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
