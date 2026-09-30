using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Profiles.Editor;

public partial class ModSourceConflictModal : UserControl
{
    public ModSourceConflictModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
