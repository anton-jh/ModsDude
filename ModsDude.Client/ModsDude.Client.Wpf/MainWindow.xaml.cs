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

namespace ModsDude.Client.Wpf;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Fires on every alt-tab, which the monitor throttles. It is worth hooking anyway: coming
        // back from a play session is exactly the moment the answer has to be fresh.
        Activated += (_, _) => (DataContext as ViewModel.Windows.MainWindowViewModel)?.NotifyWindowActivated();
    }
}