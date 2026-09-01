using Microsoft.Extensions.Logging;
using ModsDude.Client.Wpf.ViewModel.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;

namespace ModsDude.Client.Wpf.View.Behaviors;

/// <summary>
/// Kicks off <see cref="ILazyLoadable.LoadAsync"/> when an item is realized by a virtualizing
/// panel. With container recycling the same visual is handed a new item as it scrolls, which shows
/// up here as a plain property change - so this covers both first realization and every reuse.
/// </summary>
public static class LazyLoad
{
    /// <summary>
    /// Handed in at startup rather than injected: an attached behaviour is constructed by XAML and
    /// has no constructor for the container to reach. Null until then, and null in a designer, so
    /// every use is conditional.
    /// </summary>
    public static void UseDiagnostics(ILogger logger, IBackgroundProblemReporter problems)
    {
        _logger = logger;
        _problems = problems;
    }

    private static ILogger? _logger;
    private static IBackgroundProblemReporter? _problems;


    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source",
        typeof(object),
        typeof(LazyLoad),
        new PropertyMetadata(null, OnSourceChanged));


    public static object? GetSource(DependencyObject element)
        => element.GetValue(SourceProperty);

    public static void SetSource(DependencyObject element, object? value)
        => element.SetValue(SourceProperty, value);


    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not ILazyLoadable loadable)
        {
            return;
        }

        _ = Load(loadable);
    }

    private static async Task Load(ILazyLoadable loadable)
    {
        try
        {
            await loadable.LoadAsync();
        }
        catch (Exception exception)
        {
            // Nothing on screen depends on this succeeding, and there is no user action to
            // suggest. Failing quietly beats an error modal per row - but quietly is the notice and
            // the log, not nothing at all.
            _logger?.LogWarning(exception, "Lazy load of {Type} failed.", loadable.GetType().Name);
            _problems?.Report(BackgroundProblem.DeferredLoad);
        }
    }
}
