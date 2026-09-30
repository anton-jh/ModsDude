using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Profiles.Editor;

public partial class PasteModListModal : UserControl
{
    public PasteModListModal()
    {
        InitializeComponent();

        // The modal exists to receive a paste, so the box it goes in is where the caret starts.
        Loaded += (_, _) => PasteBox.Focus();
    }
}
