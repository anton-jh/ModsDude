using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Profiles;

public partial class BlockedRevisionsModal : UserControl
{
    public BlockedRevisionsModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
