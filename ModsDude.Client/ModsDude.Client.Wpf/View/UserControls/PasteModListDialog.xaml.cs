using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// Interaction logic for PasteModListDialog.xaml
/// </summary>
public partial class PasteModListDialog : UserControl
{
    public PasteModListDialog()
    {
        InitializeComponent();

        // The dialog exists to receive a paste, so the box it goes in is where the caret starts.
        Loaded += (_, _) => PasteBox.Focus();
    }
}
