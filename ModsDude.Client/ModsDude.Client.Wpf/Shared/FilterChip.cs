using System.Windows;

namespace ModsDude.Client.Wpf.Shared;

/// <summary>A count the FilterChip style shows after its label. Hidden at 0.</summary>
public static class FilterChip
{
    public static readonly DependencyProperty CountProperty = DependencyProperty.RegisterAttached(
        "Count",
        typeof(int),
        typeof(FilterChip),
        new PropertyMetadata(0));


    public static int GetCount(DependencyObject element)
        => (int)element.GetValue(CountProperty);

    public static void SetCount(DependencyObject element, int value)
        => element.SetValue(CountProperty, value);
}
