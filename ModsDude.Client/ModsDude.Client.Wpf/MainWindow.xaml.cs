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
