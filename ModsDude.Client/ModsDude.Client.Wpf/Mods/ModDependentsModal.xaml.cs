using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Mods;

public partial class ModDependentsModal : UserControl
{
    public ModDependentsModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
