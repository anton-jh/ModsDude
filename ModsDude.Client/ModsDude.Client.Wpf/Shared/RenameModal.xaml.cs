using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shared;

public partial class RenameModal : UserControl
{
    public RenameModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
