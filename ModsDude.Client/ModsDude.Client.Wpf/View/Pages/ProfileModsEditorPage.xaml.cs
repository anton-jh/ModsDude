using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.View.Pages;
/// <summary>
/// Interaction logic for ProfileModsEditorPage.xaml
/// </summary>
public partial class ProfileModsEditorPage : Page
{
    public ProfileModsEditorPage()
    {
        InitializeComponent();
    }


    /// <summary>
    /// The popup stays open through a click on its own content, so the item that runs the variant
    /// has to put the caret back up itself.
    /// </summary>
    private void CloseSaveVariants(object sender, RoutedEventArgs e)
    {
        SaveVariantButton.IsChecked = false;
    }

    /// <summary>
    /// Ctrl+F puts the caret in the search box from anywhere on the page. The narrow-then-act loop -
    /// type a few letters, take everything shown, type a few more - is the fastest way to work here,
    /// and it is only fast if getting back to the box costs nothing.
    /// </summary>
    private void PageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();

            e.Handled = true;
        }
    }

    /// <summary>
    /// Down out of the search box steps into the list it has just narrowed, which is where the
    /// arrow keys, space and Enter take over. Escape empties the box first and only gives up focus
    /// on a box that is already empty.
    /// </summary>
    private void SearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down)
        {
            AvailableList.Focus();

            e.Handled = true;
        }
        else if (e.Key is Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();

            e.Handled = true;
        }
    }
}
