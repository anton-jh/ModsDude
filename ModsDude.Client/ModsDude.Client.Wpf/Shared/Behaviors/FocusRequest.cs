using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Shared.Behaviors;

/// <summary>
/// Focuses the element each time the bound count goes up, selecting all of a text box's text, as
/// Ctrl+F does in a browser's find bar.
/// </summary>
public static class FocusRequest
{
    public static readonly DependencyProperty CountProperty = DependencyProperty.RegisterAttached(
        "Count",
        typeof(int),
        typeof(FocusRequest),
        new PropertyMetadata(0, OnCountChanged));


    public static int GetCount(DependencyObject element)
        => (int)element.GetValue(CountProperty);

    public static void SetCount(DependencyObject element, int value)
        => element.SetValue(CountProperty, value);


    private static void OnCountChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element || (int)e.NewValue <= (int)e.OldValue)
        {
            return;
        }

        // At input priority, after the element has become visible in the same change.
        element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            element.Focus();

            if (element is TextBox textBox)
            {
                textBox.SelectAll();
            }
        });
    }
}
