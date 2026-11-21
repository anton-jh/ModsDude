using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.Shared;

/// <summary>
/// A border that casts a soft shadow onto whatever it lies on: one of the app's layers - a pane, a card,
/// a modal - or the accent's glow under a primary button. The shadow is drawn as rings of faint colour
/// stacked outwards rather than by a DropShadowEffect, because an effect renders the element and
/// everything in it offscreen again on every change inside it - every scroll of a mod list in a card -
/// and turns off ClearType for all its text.
/// </summary>
public class Surface : Border
{
    private const int ShadowRings = 12;

    public static readonly DependencyProperty ShadowColorProperty = DependencyProperty.Register(
        nameof(ShadowColor), typeof(Color), typeof(Surface),
        new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShadowBlurProperty = DependencyProperty.Register(
        nameof(ShadowBlur), typeof(double), typeof(Surface),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShadowOffsetProperty = DependencyProperty.Register(
        nameof(ShadowOffset), typeof(Vector), typeof(Surface),
        new FrameworkPropertyMetadata(default(Vector), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShadowOpacityProperty = DependencyProperty.Register(
        nameof(ShadowOpacity), typeof(double), typeof(Surface),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color ShadowColor
    {
        get => (Color)GetValue(ShadowColorProperty);
        set => SetValue(ShadowColorProperty, value);
    }

    public double ShadowBlur
    {
        get => (double)GetValue(ShadowBlurProperty);
        set => SetValue(ShadowBlurProperty, value);
    }

    public Vector ShadowOffset
    {
        get => (Vector)GetValue(ShadowOffsetProperty);
        set => SetValue(ShadowOffsetProperty, value);
    }

    public double ShadowOpacity
    {
        get => (double)GetValue(ShadowOpacityProperty);
        set => SetValue(ShadowOpacityProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ShadowBlur > 0 && ShadowOpacity > 0 && ActualWidth > 0 && ActualHeight > 0)
        {
            DrawShadow(drawingContext);
        }

        base.OnRender(drawingContext);
    }

    /// <summary>
    /// Ring k covers everything within k/N of the blur from the edge, so a point in band k is under rings
    /// k to N. Each ring's alpha is chosen so that the rings over a band add up, by ordinary alpha
    /// compositing, to the shadow's strength at the middle of that band: full at the edge, easing to none
    /// at the blur's reach.
    /// </summary>
    private void DrawShadow(DrawingContext drawingContext)
    {
        var bounds = new Rect(new Point(ShadowOffset.X, ShadowOffset.Y), new Size(ActualWidth, ActualHeight));
        var ringWidth = ShadowBlur / ShadowRings;
        var coveredFromOutside = 0.0;

        for (var ring = ShadowRings; ring >= 1; ring--)
        {
            var bandMiddle = (ring - 0.5) / ShadowRings;
            var strength = ShadowOpacity * Fade(bandMiddle);
            var alpha = 1 - (1 - strength) / (1 - coveredFromOutside);
            coveredFromOutside = strength;

            var inflation = ring * ringWidth;
            var ringBounds = Rect.Inflate(bounds, inflation, inflation);
            var color = ShadowColor;
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B));
            brush.Freeze();
            drawingContext.DrawGeometry(brush, null, RoundedRectangle(ringBounds, Inflate(CornerRadius, inflation)));
        }
    }

    private static double Fade(double distance)
    {
        var remaining = 1 - distance;
        return remaining * remaining * (3 - 2 * remaining);
    }

    private static CornerRadius Inflate(CornerRadius radius, double by) => new(
        radius.TopLeft + by, radius.TopRight + by, radius.BottomRight + by, radius.BottomLeft + by);

    private static StreamGeometry RoundedRectangle(Rect rect, CornerRadius radius)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(rect.Left + radius.TopLeft, rect.Top), isFilled: true, isClosed: true);
            context.LineTo(new Point(rect.Right - radius.TopRight, rect.Top), isStroked: false, isSmoothJoin: false);
            Corner(context, new Point(rect.Right, rect.Top + radius.TopRight), radius.TopRight);
            context.LineTo(new Point(rect.Right, rect.Bottom - radius.BottomRight), isStroked: false, isSmoothJoin: false);
            Corner(context, new Point(rect.Right - radius.BottomRight, rect.Bottom), radius.BottomRight);
            context.LineTo(new Point(rect.Left + radius.BottomLeft, rect.Bottom), isStroked: false, isSmoothJoin: false);
            Corner(context, new Point(rect.Left, rect.Bottom - radius.BottomLeft), radius.BottomLeft);
            context.LineTo(new Point(rect.Left, rect.Top + radius.TopLeft), isStroked: false, isSmoothJoin: false);
            Corner(context, new Point(rect.Left + radius.TopLeft, rect.Top), radius.TopLeft);
        }

        geometry.Freeze();
        return geometry;
    }

    private static void Corner(StreamGeometryContext context, Point end, double radius) =>
        context.ArcTo(end, new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise,
            isStroked: false, isSmoothJoin: false);
}
