using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ModsDude.Client.Wpf.View.Pages;
/// <summary>
/// Interaction logic for ProfileModsEditorPage.xaml
/// </summary>
public partial class ProfileModsEditorPage : Page
{
    public ProfileModsEditorPage()
    {
        InitializeComponent();
    }


    /// <summary>
    /// WPF has no split button, so the variant lives in the caret's own context menu. Opening it on a
    /// left click is what makes the pair read as one control rather than as two buttons.
    /// </summary>
    private void OpenSaveVariants(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not ContextMenu menu)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }
}
