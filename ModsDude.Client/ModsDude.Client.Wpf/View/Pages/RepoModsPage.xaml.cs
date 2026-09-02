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
/// Interaction logic for RepoModsPage.xaml
/// </summary>
public partial class RepoModsPage : Page
{
    public RepoModsPage()
    {
        InitializeComponent();
    }


    /// <summary>
    /// The popup stays open through a click on its own content, so the item that runs the variant
    /// has to put the caret back up itself.
    /// </summary>
    private void CloseAddVariants(object sender, RoutedEventArgs e)
    {
        AddUpdatesCaret.IsChecked = false;
    }
}
