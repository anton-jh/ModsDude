using ModsDude.Client.Core.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.Shared.Behaviors;

/// <summary>
/// Fills a <see cref="TextBlock"/> with text segments, highlighting the matches of a find, and
/// scrolls the current match into view.
/// </summary>
public static class HighlightedText
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments",
        typeof(IReadOnlyList<TextSegment>),
        typeof(HighlightedText),
        new PropertyMetadata(null, OnSegmentsChanged));


    public static IReadOnlyList<TextSegment>? GetSegments(DependencyObject element)
        => (IReadOnlyList<TextSegment>?)element.GetValue(SegmentsProperty);

    public static void SetSegments(DependencyObject element, IReadOnlyList<TextSegment>? value)
        => element.SetValue(SegmentsProperty, value);


    private static void OnSegmentsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();

        Run? current = null;

        foreach (var segment in (IReadOnlyList<TextSegment>?)e.NewValue ?? [])
        {
            var run = new Run(segment.Text);

            switch (segment.Kind)
            {
                case TextSegmentKind.Match:
                    run.SetResourceReference(TextElement.BackgroundProperty, "FindMatchBrush");
                    break;

                case TextSegmentKind.CurrentMatch:
                    run.SetResourceReference(TextElement.BackgroundProperty, "FindCurrentMatchBrush");
                    run.SetResourceReference(TextElement.ForegroundProperty, "OnAccentBrush");
                    current = run;
                    break;
            }

            textBlock.Inlines.Add(run);
        }

        if (current is not null)
        {
            BringIntoView(textBlock, current);
        }
    }

    /// <summary>After layout, which is when the new runs have a position to scroll to.</summary>
    private static void BringIntoView(TextBlock textBlock, Run run)
    {
        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            textBlock.LayoutUpdated -= OnLayoutUpdated;

            if (run.Parent != textBlock)
            {
                return;
            }

            var start = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var end = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            var area = Rect.Union(start, end);

            if (area.IsEmpty)
            {
                return;
            }

            // Some room above and below, so the match isn't pinned to the edge of the view.
            var lineHeight = start.Height;
            area.Inflate(0, lineHeight * 2);

            textBlock.BringIntoView(area);
        }

        textBlock.LayoutUpdated += OnLayoutUpdated;
        textBlock.InvalidateMeasure();
    }
}
