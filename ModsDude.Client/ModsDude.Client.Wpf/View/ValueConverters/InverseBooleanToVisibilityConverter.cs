using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.View.ValueConverters;

/// <summary>
/// The other half of <see cref="BooleanToVisibilityConverter"/>, for the pairs of controls that
/// swap places - the left list's add and update buttons are one control drawn two ways, and the flag
/// that picks between them can only be read one way round.
/// </summary>
/// <remarks>
/// A converter rather than a second property on the view model, because "not this" is a fact about
/// the binding and not about the row: a model carrying both a flag and its negation has two things
/// to keep in step for the sake of one <c>Visibility</c>.
/// </remarks>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed or Visibility.Hidden;
}
