using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Wpf.Shell.Navigation;

/// <summary>
/// Every <see cref="IRetainablePage"/> that is alive, on screen or kept hidden. Pages enter through
/// <see cref="Show"/> and leave through <see cref="Release"/>, which every navigation goes through.
/// </summary>
public interface IRetainedPages : IUserScopedState
{
    /// <summary>A page was shown, kept, taken back or disposed, or one changed whether it is worth keeping.</summary>
    event Action? Changed;

    /// <summary>Starts tracking a page that was just put on screen. Does nothing for other pages.</summary>
    void Show(PageViewModel page);

    /// <summary>
    /// Takes a page off screen: keeps it hidden when it is worth keeping, and disposes it otherwise.
    /// </summary>
    void Release(PageViewModel? page);

    /// <summary>The kept page for <paramref name="key"/>, resumed and no longer kept, or null.</summary>
    T? Take<T>(object key) where T : PageViewModel, IRetainablePage;

    /// <summary>Whether a page for <paramref name="key"/> is kept hidden.</summary>
    bool IsKept(object key);

    /// <summary>Whether a page for <paramref name="key"/>, on screen or kept, holds work worth keeping.</summary>
    bool HoldsWork(object key);

    /// <summary>What every page holding work would lose, in a stable order.</summary>
    IReadOnlyList<string> DescribeAtStake();
}
