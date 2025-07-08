using ModsDude.WindowsClient.ViewModel.Windows;
using System.Windows;

namespace ModsDude.WindowsClient.Wpf;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel dataContext)
    {
        DataContext = dataContext;

        InitializeComponent();
    }
}
