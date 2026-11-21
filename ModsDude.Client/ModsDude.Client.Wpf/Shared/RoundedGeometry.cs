using System.Windows;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.Shared;

public static class RoundedGeometry
{
    public static StreamGeometry Rectangle(Rect rect, CornerRadius radius)
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
