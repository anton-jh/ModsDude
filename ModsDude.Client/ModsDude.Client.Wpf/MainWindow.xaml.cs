using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;

namespace ModsDude.Client.Wpf;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Whether the user has already answered the "something is still running" question with yes. See
    /// <see cref="OnClosing"/> for why this is a field rather than a local.
    /// </summary>
    private bool _closeConfirmed;

    /// <summary>
    /// Whether the close now under way is the user asking to leave rather than to tuck the window
    /// away. See <see cref="Quit"/>.
    /// </summary>
    private bool _quitting;


    /// <summary>
    /// Whether closing the window should hide it instead. Set by the tray, and only while there is an
    /// icon to bring it back with - null means closing quits, as it always did.
    /// </summary>
    public Func<bool>? HideOnClose { get; set; }

    /// <summary>Raised each time the window is put away, so the tray can explain where it went.</summary>
    public event EventHandler? HiddenToTray;


    public MainWindow()
    {
        InitializeComponent();

        // Two copies side by side have to be told apart at a glance - in the taskbar, and by whoever
        // is about to type into the wrong one.
        Title = Core.AppIdentity.DisplayName;

        if (Core.AppIdentity.IsProduction is false)
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(Tray.AppIcons.Uri);
        }

        // Fires on every alt-tab, which the monitor throttles. It is worth hooking anyway: coming
        // back from a play session is exactly the moment the answer has to be fresh.
        Activated += (_, _) => (DataContext as ViewModel.Windows.MainWindowViewModel)?.NotifyWindowActivated();

        SuppressBrowserNavigation();

        // Preview, so the keys reach the dialog wherever focus happens to be - including the page
        // behind it, which is where focus still is for a dialog that never asked for it.
        PreviewKeyDown += OnPreviewKeyDown;

        Closing += OnClosing;
    }


    /// <summary>
    /// Keeps the window dark from the first pixel, instead of white until the first frame lands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The flash is the native window, not WPF.</b> The window is shown a beat before the first
    /// frame of a page this size has been laid out, and in that beat Windows paints the client area
    /// itself, in the window class's brush, which is white whatever <c>ThemeMode</c> says. The title
    /// bar is already dark by then, which is what makes it read as a flash rather than as a slow
    /// start. Neither <c>Window.Background</c> (a brush the first frame draws) nor the composition
    /// target's clear colour (used once WPF renders) reaches that paint - only answering the
    /// erase-background message ourselves does.
    /// </para>
    /// <para>
    /// The colour is read off the theme rather than written down, so it stays whatever the Fluent
    /// window base is. The hook belongs to the native window, which survives being hidden to the tray,
    /// so the same fix covers coming back from it.
    /// </para>
    /// </remarks>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is not HwndSource source
            || TryFindResource("SolidBackgroundFillColorBase") is not System.Windows.Media.Color color)
        {
            return;
        }

        if (source.CompositionTarget is { } target)
        {
            target.BackgroundColor = color;
        }

        _surfaceBrush = CreateSolidBrush(color.R | (color.G << 8) | (color.B << 16));

        source.AddHook(PaintSurface);
    }

    private const int _wmEraseBackground = 0x0014;

    private IntPtr _surfaceBrush;

    private IntPtr PaintSurface(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is not _wmEraseBackground || _surfaceBrush == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        GetClientRect(hwnd, out var area);
        FillRect(wParam, ref area, _surfaceBrush);

        handled = true;

        return 1;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_surfaceBrush != IntPtr.Zero)
        {
            DeleteObject(_surfaceBrush);
            _surfaceBrush = IntPtr.Zero;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int colorRef);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref NativeRect rect, IntPtr brush);


    /// <summary>
    /// Asks before tearing the process down on top of work that is still running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cancelled and re-raised rather than answered in place.</b> The question is a modal in the
    /// shell's own slot, which means awaiting it - and <c>Closing</c> cannot be awaited: returning
    /// from the handler is what lets the close proceed. So the first pass always stops the close, and
    /// the answer closes the window itself.
    /// </para>
    /// <para>
    /// <b>Queued rather than run here, which is not a nicety.</b> <c>Close</c> called while a
    /// <c>Closing</c> dispatch is still on the stack is re-entrancy WPF answers by silently doing
    /// nothing - so a first version of this, with nothing running and nothing to ask, made the window
    /// unclosable. Posting it means the handler has returned and the close is a fresh one by the time
    /// anything decides.
    /// </para>
    /// <para>
    /// <b><see cref="_closeConfirmed"/> is what stops that being a loop.</b> It is set only by an
    /// answer, so a user who said "keep working" is asked again next time rather than having quietly
    /// spent their one refusal.
    /// </para>
    /// </remarks>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || DataContext is not ViewModel.Windows.MainWindowViewModel shell)
        {
            return;
        }

        e.Cancel = true;

        // Hiding is not leaving, so nothing that only matters on leaving is asked: whatever is
        // running keeps running behind the tray icon, which is the point of it.
        if (_quitting is false && HideOnClose?.Invoke() is true)
        {
            Hide();

            HiddenToTray?.Invoke(this, EventArgs.Empty);

            return;
        }

        _ = Dispatcher.InvokeAsync(() => ConfirmThenCloseAsync(shell)).Task.Unwrap();
    }

    private async Task ConfirmThenCloseAsync(ViewModel.Windows.MainWindowViewModel shell)
    {
        // The question is a modal in this window's own slot, so a window that is hidden to the tray
        // has to be brought back before it can be asked. Only when there is something to ask: quitting
        // an idle app from the tray should not flash the window up on the way out.
        if (shell.NeedsCloseConfirmation && IsVisible is false)
        {
            ShowFromTray();
        }

        if (await shell.ConfirmCloseAsync() is false)
        {
            // Not spent: the next close from the X hides again, and the next Quit asks again.
            _quitting = false;

            return;
        }

        _closeConfirmed = true;

        Close();
    }


    /// <summary>
    /// Brings the window back from the tray, or forward from behind whatever covers it.
    /// </summary>
    public void ShowFromTray()
    {
        if (IsVisible is false)
        {
            Show();
        }

        if (WindowState is WindowState.Minimized)
        {
            // Restores to what it was before it was minimised - maximised, here - which setting
            // WindowState to Normal would not.
            SystemCommands.RestoreWindow(this);
        }

        Activate();

        // Activate alone is refused when another app has the foreground, and the tray is by
        // definition used from somebody else's window. Briefly topmost is the accepted way round it.
        Topmost = true;
        Topmost = false;
    }

    /// <summary>
    /// Completes when the window is first on screen - immediately, if it already is. For work that
    /// needs somebody there, and was put off because the app started without a window.
    /// </summary>
    public Task WaitUntilShownAsync()
    {
        if (IsVisible)
        {
            return Task.CompletedTask;
        }

        var shown = new TaskCompletionSource();

        void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                IsVisibleChanged -= OnVisibleChanged;
                shown.TrySetResult();
            }
        }

        IsVisibleChanged += OnVisibleChanged;

        return shown.Task;
    }

    /// <summary>
    /// Asks whether the app may leave for an update, and lets the close through if so.
    /// </summary>
    /// <remarks>
    /// The same question as closing, and for the same reason: an update restarts the process, which stops
    /// whatever is running part way. Skipped where nothing is - an idle app in the tray restarts without a
    /// window flashing up on the way.
    /// </remarks>
    /// <returns>False where the user would rather it kept working.</returns>
    public async Task<bool> PrepareForRestartAsync()
    {
        if (DataContext is not ViewModel.Windows.MainWindowViewModel shell)
        {
            return true;
        }

        if (shell.NeedsCloseConfirmation)
        {
            ShowFromTray();

            if (await shell.ConfirmCloseAsync() is false)
            {
                return false;
            }
        }

        AllowClose();

        return true;
    }

    /// <summary>
    /// Leaves for real: the tray's Quit. Goes through the same close as the X, which is what puts the
    /// "something is still running" question in front of it.
    /// </summary>
    public void Quit()
    {
        _quitting = true;

        Close();
    }

    /// <summary>
    /// Lets the next close through unasked. For Windows ending the session, which is not a decision
    /// the user is making about this app - and a close that refuses to happen is what makes Windows
    /// stop a shutdown to say that ModsDude is preventing it.
    /// </summary>
    public void AllowClose() => _closeConfirmed = true;


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
