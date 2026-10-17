using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Mods.Import;

public partial class LikelyCopiesModal : UserControl
{
    public LikelyCopiesModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
