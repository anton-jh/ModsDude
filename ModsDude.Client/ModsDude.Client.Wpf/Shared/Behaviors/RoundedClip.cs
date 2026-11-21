using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shared.Behaviors;

/// <summary>
/// Clips a border's child to the inside of the border's rounded corners. A Border only rounds what it
/// draws itself, so a row that fills a list in a card or well would otherwise poke its square corners,
/// its highlight and its accent bar out past the container's curve.
/// </summary>
public static class RoundedClip
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(RoundedClip),
        new PropertyMetadata(false, OnIsEnabledChanged));


    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);


    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border)
        {
            return;
        }

        border.Loaded -= OnLayoutChanged;
        border.SizeChanged -= OnLayoutChanged;

        if (e.NewValue is true)
        {
            border.Loaded += OnLayoutChanged;
            border.SizeChanged += OnLayoutChanged;
        }
        else if (border.Child is not null)
        {
            border.Child.Clip = null;
        }
    }

    private static void OnLayoutChanged(object sender, RoutedEventArgs e) => Apply((Border)sender);

    /// <summary>
    /// The clip is the border's inner edge, in the child's coordinates: wherever padding, margin and
    /// alignment have put the child. A Border's corner radius runs along the middle of its stroke, so
    /// the inner curve is half a stroke tighter.
    /// </summary>
    private static void Apply(Border border)
    {
        if (border.Child is not { } child)
        {
            return;
        }

        var thickness = border.BorderThickness;
        var childOrigin = child.TranslatePoint(new Point(0, 0), border);
        var inner = new Rect(
            thickness.Left - childOrigin.X,
            thickness.Top - childOrigin.Y,
            Math.Max(0, border.ActualWidth - thickness.Left - thickness.Right),
            Math.Max(0, border.ActualHeight - thickness.Top - thickness.Bottom));

        var radius = border.CornerRadius;
        var innerRadius = new CornerRadius(
            Tighten(radius.TopLeft, thickness.Left, thickness.Top),
            Tighten(radius.TopRight, thickness.Right, thickness.Top),
            Tighten(radius.BottomRight, thickness.Right, thickness.Bottom),
            Tighten(radius.BottomLeft, thickness.Left, thickness.Bottom));

        child.Clip = RoundedGeometry.Rectangle(inner, innerRadius);
    }

    private static double Tighten(double radius, double side, double end)
        => Math.Max(0, radius - Math.Max(side, end) / 2);
}
