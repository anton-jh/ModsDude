using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.View.Behaviors;

/// <summary>
/// Suppresses a <see cref="ListBox"/>'s habit of extending its selection to whatever the pointer
/// passes over while the left button is held. Selection drives navigation in the sidebar, so without
/// this a drag down the menu constructs and discards one page per item on the way.
/// </summary>
/// <remarks>
/// Done by capturing the mouse on the list itself once the pointer passes the system drag threshold.
/// That takes the item containers out of the input path entirely, which is the same thing
/// <see cref="DragDrop.DoDragDrop"/> does - so a drag gesture started from the list later inherits
/// this rather than having to fight it. The item's own mouse-enter handling cannot be intercepted
/// instead: it runs as a class handler, ahead of anything an instance can attach.
/// </remarks>
public static class DragSelection
{
    public static readonly DependencyProperty SuppressedProperty =
        DependencyProperty.RegisterAttached(
            "Suppressed",
            typeof(bool),
            typeof(DragSelection),
            new PropertyMetadata(false, OnSuppressedChanged));

    private static readonly DependencyProperty OriginProperty =
        DependencyProperty.RegisterAttached(
            "Origin",
            typeof(Point?),
            typeof(DragSelection),
            new PropertyMetadata(null));


    public static bool GetSuppressed(DependencyObject element)
        => (bool)element.GetValue(SuppressedProperty);

    public static void SetSuppressed(DependencyObject element, bool value)
        => element.SetValue(SuppressedProperty, value);


    private static void OnSuppressedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list)
        {
            return;
        }

        list.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        list.PreviewMouseMove -= OnPreviewMouseMove;
        list.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        list.LostMouseCapture -= OnLostMouseCapture;

        if (e.NewValue is true)
        {
            list.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            list.PreviewMouseMove += OnPreviewMouseMove;
            list.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            list.LostMouseCapture += OnLostMouseCapture;
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;

        // Only presses that started on an item are ours. A press on the scrollbar is a drag the
        // ScrollBar itself wants to capture, and stealing it would break dragging the thumb.
        SetOrigin(list, e.OriginalSource is DependencyObject source && FindItemContainer(source) is not null
            ? e.GetPosition(list)
            : null);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var list = (ListBox)sender;

        if (e.LeftButton is not MouseButtonState.Pressed)
        {
            SetOrigin(list, null);
            return;
        }

        if (GetOrigin(list) is not Point origin || list.IsMouseCaptured)
        {
            return;
        }

        var position = e.GetPosition(list);

        if (Math.Abs(position.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (Mouse.Captured is null)
        {
            list.CaptureMouse();
        }
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;

        SetOrigin(list, null);

        if (list.IsMouseCaptured)
        {
            list.ReleaseMouseCapture();
        }
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        SetOrigin((ListBox)sender, null);
    }


    private static Point? GetOrigin(DependencyObject element)
        => (Point?)element.GetValue(OriginProperty);

    private static void SetOrigin(DependencyObject element, Point? value)
        => element.SetValue(OriginProperty, value);

    private static ListBoxItem? FindItemContainer(DependencyObject source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is ListBoxItem container)
            {
                return container;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
