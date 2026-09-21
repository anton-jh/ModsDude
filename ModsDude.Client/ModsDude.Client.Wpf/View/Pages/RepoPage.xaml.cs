using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
/// Interaction logic for RepoPage.xaml
/// </summary>
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
