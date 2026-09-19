using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// Interaction logic for UnrecognisedFilesDialog.xaml
/// </summary>
public partial class UnrecognisedFilesDialog : UserControl
{
    public UnrecognisedFilesDialog()
    {
        InitializeComponent();
        Loaded += (_, __) => Focus();
    }
}
