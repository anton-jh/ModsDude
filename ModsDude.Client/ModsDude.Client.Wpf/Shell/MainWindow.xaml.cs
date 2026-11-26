using ModsDude.Client.Wpf.Shell.Modals;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ModsDude.Client.Wpf.Shell;
public partial class MainWindow : Window
{
    private bool _closeConfirmed;


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
        Activated += (_, _) => (DataContext as MainWindowViewModel)?.NotifyWindowActivated();

        SuppressBrowserNavigation();

        // Preview, so the keys reach the modal wherever focus happens to be - including the page
        // behind it, which is where focus still is for a modal that never asked for it.
        PreviewKeyDown += OnPreviewKeyDown;

        Closing += OnClosing;

        SizeChanged += (_, _) => KeepOnScreenWhenMaximized();
        StateChanged += (_, _) => KeepOnScreenWhenMaximized();
    }


    /// <summary>
    /// Pulls the content back inside the screen while the window is maximised.
    /// </summary>
    /// <remarks>
    /// Windows maximises a window so that its frame hangs off the screen and only the client area shows.
    /// With <c>WindowChrome</c> there is no frame - the client area is the whole window - so the same
    /// overhang takes the top of the title bar and the edges of everything else with it. The overhang is
    /// measured against the monitor's work area rather than assumed from a system metric, so it is right
    /// on whichever monitor and at whichever scale the window is maximised on.
    /// </remarks>
    private void KeepOnScreenWhenMaximized()
    {
        var inset = new Thickness(0);

        if (WindowState is WindowState.Maximized
            && new WindowInteropHelper(this).Handle is var hwnd and not 0
            && GetWindowRect(hwnd, out var window)
            && MonitorFromWindow(hwnd, _monitorDefaultToNearest) is var monitor and not 0)
        {
            var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };

            if (GetMonitorInfo(monitor, ref info))
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                var work = info.WorkArea;

                inset = new Thickness(
                    Math.Max(0, work.Left - window.Left) / dpi.DpiScaleX,
                    Math.Max(0, work.Top - window.Top) / dpi.DpiScaleY,
                    Math.Max(0, window.Right - work.Right) / dpi.DpiScaleX,
                    Math.Max(0, window.Bottom - work.Bottom) / dpi.DpiScaleY);
            }
        }

        Root.Margin = inset;
    }

    private const int _monitorDefaultToNearest = 2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);


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
    /// Hides to the tray where the tray says so, and otherwise hands the close to the shell's exit,
    /// which asks first and shuts the app down itself.
    /// </summary>
    /// <remarks>
    /// The close is always refused here and the exit posted, because <c>Closing</c> cannot be awaited
    /// and a close or shutdown started while it is still on the stack is silently ignored.
    /// </remarks>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainWindowViewModel shell)
        {
            return;
        }

        e.Cancel = true;

        if (HideOnClose?.Invoke() is true)
        {
            Hide();

            HiddenToTray?.Invoke(this, EventArgs.Empty);

            return;
        }

        Dispatcher.InvokeAsync(() => shell.QuitCommand.Execute(null));
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

    /// <summary>Lets the next close through unasked, for an exit that has already been decided.</summary>
    public void AllowClose() => _closeConfirmed = true;


    /// <summary>
    /// Escape, Enter and Ctrl+F for whatever modal is up, asked of the modal itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here rather than on each modal.</b> An <c>InputBinding</c> on a modal only fires when focus
    /// is inside it, and focus is often still on the page behind. The shell always has the focus
    /// this needs, and it has exactly one modal.
    /// </para>
    /// <para>
    /// <b>What the keys mean is still the modal's to say</b> - see
    /// <see cref="ModalViewModel.TryCancel"/>. This only decides <em>whether to
    /// ask</em>, which is a question about the focused control rather than about the modal.
    /// </para>
    /// </remarks>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel shell
            || shell.Modal is not ModalViewModel modal)
        {
            return;
        }

        // Taken whether or not the modal finds anything, so the page behind doesn't focus its own search.
        if (e.Key is Key.F && Keyboard.Modifiers is ModifierKeys.Control)
        {
            modal.Find();
            e.Handled = true;

            return;
        }

        // Enter with a modifier is the focused control's, such as Shift+Enter for the previous match.
        if (e.Key is not (Key.Escape or Key.Enter)
            || (e.Key is Key.Enter && Keyboard.Modifiers is not ModifierKeys.None)
            || BelongsToFocus(e.Key))
        {
            return;
        }

        e.Handled = e.Key is Key.Escape ? modal.TryCancel() : modal.TryAccept();
    }


    private bool _scrimPressed;

    private void OnScrimMouseDown(object sender, MouseButtonEventArgs e)
    {
        _scrimPressed = ReferenceEquals(e.OriginalSource, ModalScrim);
    }

    /// <summary>
    /// Dismisses the modal only when the press started on the scrim too, so letting go of a text
    /// selection dragged out of the modal doesn't close it.
    /// </summary>
    private void OnScrimMouseUp(object sender, MouseButtonEventArgs e)
    {
        var pressedHere = _scrimPressed;
        _scrimPressed = false;

        if (pressedHere
            && ReferenceEquals(e.OriginalSource, ModalScrim)
            && DataContext is MainWindowViewModel shell)
        {
            shell.DismissModalCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Whether the focused control is already using this key for something of its own.
    /// </summary>
    /// <remarks>
    /// <b>Three cases, and all three are ones where taking the key would break something the user can
    /// see.</b> An open drop-down uses both keys to pick and to abandon; a focused button is the
    /// thing Enter presses, and stealing it would confirm a modal whose Cancel the user had just
    /// tabbed to; and a box that takes newlines is one where Enter types rather than submits.
    /// Escape is never any of the last two, so it passes through to the modal from a text box -
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
