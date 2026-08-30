using System.Globalization;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.View.ValueConverters;

/// <summary>
/// Binds one enum property to a group of radio buttons, each naming its value in
/// <c>ConverterParameter</c>. The group's own exclusivity then does the bookkeeping, so the view
/// model carries the selected value and nothing else.
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not null
            && parameter is string name
            && string.Equals(value.ToString(), name, StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Unchecking is what the group does to the previously selected button on its way to
        // selecting another one; writing that back would clear the property in between.
        if (value is not true || parameter is not string name)
        {
            return Binding.DoNothing;
        }

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return enumType.IsEnum && Enum.TryParse(enumType, name, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}
