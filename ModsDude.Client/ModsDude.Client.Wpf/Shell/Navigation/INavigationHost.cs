namespace ModsDude.Client.Wpf.Shell.Navigation;

/// <summary>A page that shows pages of its own, such as a repo's tabs.</summary>
public interface INavigationHost
{
    NavigationManager NavManager { get; }
}
