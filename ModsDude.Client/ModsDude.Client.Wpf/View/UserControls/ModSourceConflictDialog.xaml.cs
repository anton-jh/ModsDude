using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// Interaction logic for ModSourceConflictDialog.xaml
/// </summary>
public partial class ModSourceConflictDialog : UserControl
{
    public ModSourceConflictDialog()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
