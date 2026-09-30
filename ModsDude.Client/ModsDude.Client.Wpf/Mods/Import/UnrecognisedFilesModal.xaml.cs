using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Mods.Import;

public partial class UnrecognisedFilesModal : UserControl
{
    public UnrecognisedFilesModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
