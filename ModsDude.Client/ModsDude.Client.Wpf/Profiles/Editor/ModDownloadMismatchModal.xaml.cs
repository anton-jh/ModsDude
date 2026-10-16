using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Profiles.Editor;

public partial class ModDownloadMismatchModal : UserControl
{
    public ModDownloadMismatchModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
