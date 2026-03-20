using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Globalization;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.View.ValueConverters;
public class IconKindToStringConverter : IValueConverter
{
    private const string _warning = "\xE7BA";
    private const string _question = "\xE9CE";
    private const string _error = "\xEA39";


    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IconKind icon)
            return string.Empty;

        return icon switch
        {
            IconKind.None => "",
            IconKind.Warning => _warning,
            IconKind.Question => _question,
            IconKind.Error => _error,
            _ => ""
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string str)
            return Binding.DoNothing;

        return str switch
        {
            "" => IconKind.None,
            _warning => IconKind.Warning,
            _question => IconKind.Question,
            _error => IconKind.Error,
            _ => Binding.DoNothing
        };
    }
}
