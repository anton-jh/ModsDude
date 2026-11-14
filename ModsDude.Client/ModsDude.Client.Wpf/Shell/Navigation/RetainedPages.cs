using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell.Navigation;

/// <inheritdoc cref="IRetainedPages"/>
/// <remarks>Used on the UI thread only, like the pages it holds.</remarks>
public sealed class RetainedPages(ILogger<RetainedPages> logger) : IRetainedPages
{
    private readonly List<Entry> _entries = [];


    public event Action? Changed;


    public void Show(PageViewModel page)
    {
        if (page is not IRetainablePage retainable || Find(retainable) is not null)
        {
            return;
        }

        _entries.Add(new Entry(retainable) { WasWorthRetaining = retainable.IsWorthRetaining });
        retainable.PropertyChanged += OnPageChanged;

        Changed?.Invoke();
    }

    public void Release(PageViewModel? page)
    {
        if (page is IRetainablePage retainable && Find(retainable) is Entry entry)
        {
            if (retainable.IsWorthRetaining)
            {
                if (entry.IsKept is false)
                {
                    entry.IsKept = true;
                    retainable.Suspend();

                    logger.LogInformation("Kept a page hidden: {Description}.", retainable.RetentionDescription);

                    Changed?.Invoke();
                }

                return;
            }

            Forget(entry);
        }

        (page as IDisposable)?.Dispose();
    }

    public T? Take<T>(object key) where T : PageViewModel, IRetainablePage
    {
        if (_entries.FirstOrDefault(x => x.IsKept && Equals(x.Page.RetentionKey, key)) is not { Page: T page } entry)
        {
            return null;
        }

        entry.IsKept = false;
        page.Resume();

        Changed?.Invoke();

        return page;
    }

    public bool IsKept(object key) => _entries.Any(x => x.IsKept && Equals(x.Page.RetentionKey, key));

    public bool HoldsWork(object key) => _entries.Any(x => x.Page.IsWorthRetaining && Equals(x.Page.RetentionKey, key));

    public IReadOnlyList<string> DescribeAtStake()
        => [.. _entries
            .Where(x => x.Page.IsWorthRetaining)
            .Select(x => x.Page.RetentionDescription)
            .Order(StringComparer.Ordinal)];

    /// <summary>The shell built from the previous account has been released by now, so what is left here is kept pages.</summary>
    public void ClearUserState()
    {
        foreach (var entry in _entries.ToList())
        {
            Forget(entry);
            entry.Page.Dispose();
        }
    }


    private Entry? Find(IRetainablePage page) => _entries.FirstOrDefault(x => ReferenceEquals(x.Page, page));

    private void Forget(Entry entry)
    {
        entry.Page.PropertyChanged -= OnPageChanged;
        _entries.Remove(entry);

        Changed?.Invoke();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IRetainablePage.IsWorthRetaining) or null or "")
            || sender is not IRetainablePage page
            || Find(page) is not Entry entry
            || entry.WasWorthRetaining == page.IsWorthRetaining)
        {
            return;
        }

        entry.WasWorthRetaining = page.IsWorthRetaining;

        Changed?.Invoke();

        if (entry.IsKept && page.IsWorthRetaining is false)
        {
            // Not from inside the page's own notification, which is still part way through changing it.
            Application.Current.Dispatcher.BeginInvoke(() => DropIfDone(entry));
        }
    }

    private void DropIfDone(Entry entry)
    {
        if (_entries.Contains(entry) is false || entry.IsKept is false || entry.Page.IsWorthRetaining)
        {
            return;
        }

        logger.LogInformation("Dropped a kept page that no longer holds work: {Key}.", entry.Page.RetentionKey);

        Forget(entry);
        entry.Page.Dispose();
    }


    private sealed class Entry(IRetainablePage page)
    {
        public IRetainablePage Page { get; } = page;

        public bool IsKept { get; set; }

        public bool WasWorthRetaining { get; set; }
    }
}
