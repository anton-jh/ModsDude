using ModsDude.Client.Wpf.Shared;
using System.Windows;

namespace ModsDude.Client.Wpf.Repos;
public partial class RepoOverviewPage : AppPage
{
    public RepoOverviewPage()
    {
        InitializeComponent();
    }

    private void CloseDeactivateMenu(object sender, RoutedEventArgs e)
    {
        DeactivateToggle.IsChecked = false;
    }
}
