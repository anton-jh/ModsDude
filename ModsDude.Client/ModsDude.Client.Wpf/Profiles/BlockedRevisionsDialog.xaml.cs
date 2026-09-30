using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Profiles;

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
