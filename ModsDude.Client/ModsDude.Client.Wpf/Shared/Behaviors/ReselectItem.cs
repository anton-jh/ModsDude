using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.Shared.Behaviors;

/// <summary>
/// Runs a command when the user clicks, or presses Enter on, the item a <see cref="ListBox"/> already
/// has selected. The list raises no selection change for that, so a menu uses this to leave a sub-page
/// for the entry highlighted above it.
/// </summary>
public static class ReselectItem
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(ReselectItem),
        new PropertyMetadata(null, OnCommandChanged));


    public static ICommand? GetCommand(DependencyObject element)
        => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value)
        => element.SetValue(CommandProperty, value);


    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list)
        {
            return;
        }

        list.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        list.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is ICommand)
        {
            list.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            list.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    /// <summary>
    /// Read on the way down, before the item handles the press, so <c>IsSelected</c> is still what it
    /// was before this click.
    /// </summary>
    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;

        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(list, source) is ListBoxItem { IsSelected: true })
        {
            Execute(list);
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var list = (ListBox)sender;

        if (e.Key is Key.Enter
            && e.OriginalSource is ListBoxItem { IsSelected: true } item
            && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), list))
        {
            Execute(list);
        }
    }

    private static void Execute(ListBox list)
    {
        if (GetCommand(list) is ICommand command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
