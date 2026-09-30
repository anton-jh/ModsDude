using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Repos;
public partial class RepoPage : Page
{
    public RepoPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Puts the deactivation menu away once one of its items has been pressed - the command is the
    /// item's own, so the caret has no way to know it ran.
    /// </summary>
    private void CloseDeactivateMenu(object sender, RoutedEventArgs e)
    {
        DeactivateCaret.IsChecked = false;
    }
}
