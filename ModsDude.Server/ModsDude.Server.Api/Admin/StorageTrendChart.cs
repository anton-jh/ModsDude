using ModsDude.Server.Domain.Statistics;
using ModsDude.Server.Persistence.Extensions.EntityExtensions;
using System.Globalization;

namespace ModsDude.Server.Api.Admin;

/// <summary>
/// The geometry of a line chart of stored bytes per container per day, in SVG user units.
/// </summary>
public class StorageTrendChart
{
    public const double Width = 800;
    public const double Height = 240;
    public const double PlotLeft = 72;
    public const double PlotRight = Width - 88;
    public const double PlotTop = 12;
    public const double PlotBottom = Height - 28;

    private const int _gridLines = 4;
    private const double _labelSpacing = 13;


    public StorageTrendChart(IReadOnlyList<StorageDailyTotal> totals)
    {
        Dates = [.. totals.Select(x => x.Date).Distinct().Order()];

        var max = totals.Count == 0 ? 0 : totals.Max(x => x.StoredBytes);
        Top = NiceCeiling(max);

        Series = [.. Enum.GetValues<StorageContainer>()
            .Select((container, index) =>
            {
                var byDate = totals.Where(x => x.Container == container).ToDictionary(x => x.Date, x => x.StoredBytes);
                var values = Dates.Select(x => byDate.GetValueOrDefault(x)).ToList();

                return new ChartSeries(container, index + 1, values, PointsFor(values));
            })];

        GridLines = [.. Enumerable.Range(0, _gridLines + 1)
            .Select(x => Top * x / _gridLines)
            .Select(x => new GridLine(Y(x), AdminFormat.Bytes(x)))];

        EndLabels = Dates.Count == 0 ? [] : SpreadEndLabels();
    }


    public IReadOnlyList<DateOnly> Dates { get; }
    public IReadOnlyList<ChartSeries> Series { get; }
    public IReadOnlyList<GridLine> GridLines { get; }

    /// <summary>Each series named beside its last point, pushed apart where they would overlap.</summary>
    public IReadOnlyList<EndLabel> EndLabels { get; }

    /// <summary>The value at the top of the plot.</summary>
    public long Top { get; }

    public string ViewBox => $"0 0 {Number(Width)} {Number(Height)}";


    public double X(int dateIndex)
    {
        if (Dates.Count < 2)
        {
            return (PlotLeft + PlotRight) / 2;
        }

        var span = Dates[^1].DayNumber - Dates[0].DayNumber;
        var offset = Dates[dateIndex].DayNumber - Dates[0].DayNumber;

        return PlotLeft + (PlotRight - PlotLeft) * offset / span;
    }

    public double Y(long bytes)
    {
        return Top == 0
            ? PlotBottom
            : PlotBottom - (PlotBottom - PlotTop) * bytes / Top;
    }

    /// <summary>Per date, where it sits and what each series held, for the hover tooltip.</summary>
    public IReadOnlyList<HoverPoint> HoverPoints()
    {
        return [.. Dates.Select((date, index) => new HoverPoint(
            X(index),
            AdminFormat.Date(date),
            [.. Series.Select(x => AdminFormat.Bytes(x.Values[index]))]))];
    }

    public static string Number(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }


    private List<EndLabel> SpreadEndLabels()
    {
        var labels = new List<EndLabel>();
        var below = double.MinValue;

        foreach (var series in Series.OrderBy(x => Y(x.Values[^1])).ThenBy(x => x.Slot))
        {
            var y = Math.Max(Y(series.Values[^1]), below + _labelSpacing);
            labels.Add(new EndLabel(series.Container, X(Dates.Count - 1) + 8, y));
            below = y;
        }

        // Pushed past the bottom of the chart: shift the whole stack back up.
        var overflow = labels[^1].Y - (Height - 4);
        if (overflow > 0)
        {
            labels = [.. labels.Select(x => x with { Y = x.Y - overflow })];
        }

        return labels;
    }

    private string PointsFor(IReadOnlyList<long> values)
    {
        return string.Join(" ", values.Select((value, index) => $"{Number(X(index))},{Number(Y(value))}"));
    }

    /// <summary>The smallest 1, 2 or 5 times a power of 1024 at or above <paramref name="value"/>.</summary>
    private static long NiceCeiling(long value)
    {
        if (value <= 0)
        {
            return 1024;
        }

        long unit = 1;
        while (unit <= value / 1024)
        {
            unit *= 1024;
        }

        foreach (var step in new long[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 1024 })
        {
            if (unit * step >= value)
            {
                return unit * step;
            }
        }

        return unit * 1024;
    }


    /// <param name="Slot">The categorical colour slot, from 1.</param>
    public record ChartSeries(StorageContainer Container, int Slot, IReadOnlyList<long> Values, string Points);

    public record GridLine(double Y, string Label);

    public record EndLabel(StorageContainer Container, double X, double Y);

    public record HoverPoint(double X, string Date, IReadOnlyList<string> Values);
}
