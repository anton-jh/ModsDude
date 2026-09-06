using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// Interaction logic for BlockedRevisionsDialog.xaml
/// </summary>
public partial class BlockedRevisionsDialog : UserControl
{
    public BlockedRevisionsDialog()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
