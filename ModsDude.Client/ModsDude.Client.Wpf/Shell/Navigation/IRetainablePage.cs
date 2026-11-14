using System.ComponentModel;

namespace ModsDude.Client.Wpf.Shell.Navigation;

/// <summary>
/// A page that is kept alive, hidden, when it is navigated away from while it holds work that would
/// otherwise be lost: a draft with unsaved changes, or a save still running. Going back to it shows the
/// same page again instead of a new one.
/// </summary>
/// <remarks>
/// For pages that are a draft the user works on, with a lot of state behind it. Raises
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> for <see cref="IsWorthRetaining"/>.
/// </remarks>
public interface IRetainablePage : INotifyPropertyChanged, IDisposable
{
    /// <summary>What the page is for. Opening a page with an equal key shows the kept one.</summary>
    object RetentionKey { get; }

    /// <summary>
    /// Whether the page holds work worth keeping, and that work can still be finished. A kept page that
    /// turns false is disposed.
    /// </summary>
    bool IsWorthRetaining { get; }

    /// <summary>What is lost if the page goes, as listed before the app closes.</summary>
    string RetentionDescription { get; }

    /// <summary>Lets go of what only a page on screen should hold.</summary>
    void Suspend();

    /// <summary>Takes back what <see cref="Suspend"/> let go of, just before the page is shown again.</summary>
    void Resume();
}
