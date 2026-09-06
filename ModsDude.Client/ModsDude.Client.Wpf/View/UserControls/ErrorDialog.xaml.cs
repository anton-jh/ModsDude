using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// Interaction logic for ErrorDialog.xaml
/// </summary>
public partial class ErrorDialog : UserControl
{
    public ErrorDialog()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
