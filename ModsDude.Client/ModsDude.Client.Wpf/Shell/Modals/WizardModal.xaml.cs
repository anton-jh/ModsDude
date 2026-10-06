using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shell.Modals;

public partial class WizardModal : UserControl
{
    public WizardModal()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
