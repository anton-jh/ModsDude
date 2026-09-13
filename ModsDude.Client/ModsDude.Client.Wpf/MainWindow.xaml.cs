using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace ModsDude.Client.Wpf;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Fires on every alt-tab, which the monitor throttles. It is worth hooking anyway: coming
        // back from a play session is exactly the moment the answer has to be fresh.
        Activated += (_, _) => (DataContext as ViewModel.Windows.MainWindowViewModel)?.NotifyWindowActivated();

        SuppressBrowserNavigation();

        // Preview, so the keys reach the dialog wherever focus happens to be - including the page
        // behind it, which is where focus still is for a dialog that never asked for it.
        PreviewKeyDown += OnPreviewKeyDown;
    }


    /// <summary>
    /// Escape and Enter for whatever modal is up, asked of the modal itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here rather than on each dialog.</b> Every dialog used to carry its own <c>KeyBinding</c>
    /// pair, and an <c>InputBinding</c> only fires when focus is inside the element carrying it -
    /// so the nine dialogs that never called <c>Focus()</c> had an Escape that did nothing at all,
    /// and which of the seventeen worked was decided by a line of constructor code nothing connected
    /// to the binding. The shell always has the focus this needs, and it has exactly one modal.
    /// </para>
    /// <para>
    /// <b>What the keys mean is still the dialog's to say</b> - see
    /// <see cref="ViewModel.ViewModels.ModalViewModel.TryCancel"/>. This only decides <em>whether to
    /// ask</em>, which is a question about the focused control rather than about the dialog.
    /// </para>
    /// </remarks>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.Enter)
            || DataContext is not ViewModel.Windows.MainWindowViewModel shell
            || shell.Modal is not ViewModel.ViewModels.ModalViewModel modal
            || BelongsToFocus(e.Key))
        {
            return;
        }

        e.Handled = e.Key is Key.Escape ? modal.TryCancel() : modal.TryAccept();
    }

    /// <summary>
    /// Whether the focused control is already using this key for something of its own.
    /// </summary>
    /// <remarks>
    /// <b>Three cases, and all three are ones where taking the key would break something the user can
    /// see.</b> An open drop-down uses both keys to pick and to abandon; a focused button is the
    /// thing Enter presses, and stealing it would confirm a dialog whose Cancel the user had just
    /// tabbed to; and a box that takes newlines is one where Enter types rather than submits.
    /// Escape is never any of the last two, so it passes through to the dialog from a text box -
    /// which is the whole point of asking at the shell.
    /// </remarks>
    private static bool BelongsToFocus(Key key)
    {
        return Keyboard.FocusedElement switch
        {
            System.Windows.Controls.ComboBox { IsDropDownOpen: true } => true,
            System.Windows.Controls.Primitives.ButtonBase => key is Key.Enter,
            System.Windows.Controls.TextBox { AcceptsReturn: true } => key is Key.Enter,
            _ => false
        };
    }


    /// <summary>
    /// Takes the back and forward gestures away from the <c>Frame</c>s the shell is built out of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This app has no history to go back through.</b> Navigation here is a sidebar selection, and
    /// selection is refusable - <c>NavigationManager</c> asks before leaving a page with unsaved
    /// changes and pushes the selection back if the answer is no. A <c>Frame</c>'s own journal knows
    /// none of that: it swaps its content directly, which walked straight past the lock and left the
    /// sidebar highlighting a page that was no longer on screen.
    /// </para>
    /// <para>
    /// So the gestures are removed rather than rerouted, which is the honest of the two options: there
    /// is no ordering of visited pages to walk, only a tree of menus, and inventing one would be a
    /// second navigation model to keep in agreement with the first. That means the mouse's back and
    /// forward buttons, and the keyboard equivalents WPF binds to the same commands - Alt+Left,
    /// Alt+Right and Backspace - all do nothing.
    /// </para>
    /// <para>
    /// Both halves are needed. The command binding covers everything routed as
    /// <see cref="NavigationCommands.BrowseBack"/>, and the mouse handler covers the buttons
    /// themselves, which a <c>Frame</c> reads off <c>MouseDown</c> rather than through the command.
    /// </para>
    /// </remarks>
    private void SuppressBrowserNavigation()
    {
        CommandBindings.Add(new CommandBinding(NavigationCommands.BrowseBack, Refuse, CannotExecute));
        CommandBindings.Add(new CommandBinding(NavigationCommands.BrowseForward, Refuse, CannotExecute));
        CommandBindings.Add(new CommandBinding(NavigationCommands.GoToPage, Refuse, CannotExecute));

        PreviewMouseDown += OnPreviewMouseDown;
        PreviewMouseUp += OnPreviewMouseDown;
    }

    private static void Refuse(object sender, ExecutedRoutedEventArgs e) => e.Handled = true;

    private static void CannotExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = false;
        e.Handled = true;
    }

    /// <summary>
    /// Swallows the two side buttons, on the way down and the way up.
    /// </summary>
    /// <remarks>
    /// Both, because handling only one of them leaves the other to be delivered on its own, and a
    /// control that sees an up without a down is being told something that did not happen.
    /// </remarks>
    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            e.Handled = true;
        }
    }
}
