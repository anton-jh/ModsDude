namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// A view model that defers some of its work until it is actually on screen. Wire it up with
/// <c>b:LazyLoad.Source="{Binding}"</c> on the root of the item template.
/// </summary>
public interface ILazyLoadable
{
    /// <summary>
    /// Called when the item becomes visible. Must be safe to call repeatedly - list virtualization
    /// realizes the same item every time it scrolls back into view.
    /// </summary>
    Task LoadAsync();
}
