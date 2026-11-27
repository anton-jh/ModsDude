using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Games;

/// <summary>The Deactivate button and its two choices, for a <see cref="GameStatusViewModel"/>.</summary>
public partial class DeactivateGameMenu : UserControl
{
    public DeactivateGameMenu()
    {
        InitializeComponent();
    }

    private void CloseMenu(object sender, RoutedEventArgs e)
    {
        DeactivateToggle.IsChecked = false;
    }
}
