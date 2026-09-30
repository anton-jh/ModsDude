using ModsDude.Client.Wpf.Shared.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.Profiles.Editor;
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

    /// <inheritdoc cref="CloseSaveVariants"/>
    private void CloseUpdateVariants(object sender, RoutedEventArgs e)
    {
        UpdateVariantButton.IsChecked = false;
    }

    /// <summary>
    /// Ctrl+F puts the caret in the search box. Ctrl+Z and Ctrl+Y undo and redo the draft - except in a
    /// text box, where they are the text's own.
    /// </summary>
    private void PageKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key is Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();

            e.Handled = true;

            return;
        }

        if (e.Key is not (Key.Z or Key.Y)
            || Keyboard.FocusedElement is TextBox
            || DataContext is not ProfileModsEditorPageViewModel page)
        {
            return;
        }

        var command = e.Key is Key.Z ? page.UndoCommand : page.RedoCommand;

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Down out of the search box steps into the list it has just narrowed, which is where the
    /// arrow keys, space and Enter take over. Escape empties the box first and only gives up focus
    /// on a box that is already empty. Neither while the completion list is open, whose keys those
    /// are - see <see cref="Behaviors.SearchCompletion"/>.
    /// </summary>
    private void SearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (SearchCompletion.IsOpen(SearchBox))
        {
            return;
        }

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
