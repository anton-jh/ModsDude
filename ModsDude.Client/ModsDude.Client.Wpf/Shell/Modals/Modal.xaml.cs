using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shell.Modals;
public partial class Modal : UserControl
{
    public Modal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
