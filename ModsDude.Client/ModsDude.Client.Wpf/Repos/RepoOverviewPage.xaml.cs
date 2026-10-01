using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Repos;
public partial class RepoOverviewPage : Page
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
