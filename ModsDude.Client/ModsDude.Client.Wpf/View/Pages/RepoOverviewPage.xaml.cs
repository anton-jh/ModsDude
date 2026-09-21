using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ModsDude.Client.Wpf.View.Pages;
/// <summary>
/// Interaction logic for RepoOverviewPage.xaml
/// </summary>
public partial class RepoOverviewPage : Page
{
    public RepoOverviewPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Puts a row's deactivation menu away once one of its items has been pressed. The menu is in the
    /// row's template, so it is reached through the toggle that opened it, which each item carries as
    /// its tag.
    /// </summary>
    private void CloseDeactivateMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToggleButton toggle })
        {
            toggle.IsChecked = false;
        }
    }
}
