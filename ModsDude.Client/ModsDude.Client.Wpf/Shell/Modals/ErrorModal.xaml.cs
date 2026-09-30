using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shell.Modals;

public partial class ErrorModal : UserControl
{
    public ErrorModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
