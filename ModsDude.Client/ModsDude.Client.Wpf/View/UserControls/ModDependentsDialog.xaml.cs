using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// Interaction logic for ModDependentsDialog.xaml
/// </summary>
public partial class ModDependentsDialog : UserControl
{
    public ModDependentsDialog()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
