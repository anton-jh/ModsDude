using System.Windows.Controls;

namespace ModsDude.Client.Wpf.Shared;

public partial class NameModal : UserControl
{
    public NameModal()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }
}
