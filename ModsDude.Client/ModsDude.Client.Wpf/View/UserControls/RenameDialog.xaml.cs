using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// Interaction logic for RenameDialog.xaml
/// </summary>
public partial class RenameDialog : UserControl
{
    public RenameDialog()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
