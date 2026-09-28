using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// A 48px title bar for a window whose own has been taken away with <see cref="WindowChrome"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Windows is told what each part of the bar is, rather than WPF acting it out.</b> The window's
/// <c>WindowChrome</c> has a caption height of zero, so without this every pixel is plain client area.
/// Answering <c>WM_NCHITTEST</c> with <c>HTCAPTION</c> over the empty part of the bar hands dragging,
/// double-click to maximise, Aero Snap and the right-click system menu back to Windows, which does all of
/// them better than a <c>DragMove</c> would.
/// </para>
/// <para>
/// <b>The maximise button answers as <c>HTMAXBUTTON</c>,</b> which is the only way to get Windows 11's
/// Snap Layouts flyout on hover. That makes it non-client area, which WPF does not see the mouse over, so
/// its hover, press and click are all driven from the non-client messages here instead.
/// </para>
/// <para>
/// Minimise and close stay client area and are ordinary buttons.
/// </para>
/// </remarks>
public partial class WindowTitleBar : UserControl
{
    private const int _wmNcHitTest = 0x0084;
    private const int _wmNcLeftButtonDown = 0x00A1;
    private const int _wmNcLeftButtonUp = 0x00A2;
    private const int _wmNcMouseLeave = 0x02A2;

    private const int _htCaption = 2;
    private const int _htMaxButton = 9;

    private Window? _window;
    private HwndSource? _source;


    public WindowTitleBar()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }


    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);

        if (_window is null || PresentationSource.FromVisual(_window) is not HwndSource source)
        {
            return;
        }

        // Added after WindowChrome's own hook, which puts it first in line: WPF calls the most recently
        // added hook first, and the chrome would otherwise answer every hit test inside the bar as client.
        _source = source;
        _source.AddHook(WndProc);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _source?.RemoveHook(WndProc);
        _source = null;
    }


    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            SystemCommands.MinimizeWindow(_window);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        // Through the window's own close, so the tray's hide-on-close and the "still running" question
        // both still get their say.
        _window?.Close();
    }

    private void ToggleMaximized()
    {
        if (_window is null)
        {
            return;
        }

        if (_window.WindowState is WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(_window);
        }
        else
        {
            SystemCommands.MaximizeWindow(_window);
        }
    }


    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case _wmNcHitTest:
                return HitTest(lParam, ref handled);

            case _wmNcLeftButtonDown when wParam.ToInt32() is _htMaxButton:
                MaximizeButton.IsPushed = true;
                handled = true;
                return IntPtr.Zero;

            case _wmNcLeftButtonUp when wParam.ToInt32() is _htMaxButton:
                // Only a press that started on the button counts, the way a click does anywhere else.
                if (MaximizeButton.IsPushed)
                {
                    MaximizeButton.IsPushed = false;
                    ToggleMaximized();
                }
                handled = true;
                return IntPtr.Zero;

            case _wmNcMouseLeave:
                MaximizeButton.IsHovered = false;
                MaximizeButton.IsPushed = false;
                return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private IntPtr HitTest(IntPtr lParam, ref bool handled)
    {
        var screen = new Point(
            (short)(lParam.ToInt64() & 0xFFFF),
            (short)((lParam.ToInt64() >> 16) & 0xFFFF));

        var overMaximize = Contains(MaximizeButton, screen);

        MaximizeButton.IsHovered = overMaximize;

        if (_window is null || Contains(this, screen) is false || OnResizeBorder(screen))
        {
            return IntPtr.Zero;
        }

        if (overMaximize)
        {
            handled = true;
            return _htMaxButton;
        }

        if (Contains(MinimizeButton, screen) || Contains(CloseButton, screen))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return _htCaption;
    }

    /// <summary>
    /// Whether the point is on the window's top, left or right edge, which resizes rather than drags.
    /// Left to the window's chrome to answer. Never while maximised: there is no edge to pull.
    /// </summary>
    private bool OnResizeBorder(Point screen)
    {
        if (_window is null || _window.WindowState is WindowState.Maximized)
        {
            return false;
        }

        var border = WindowChrome.GetWindowChrome(_window)?.ResizeBorderThickness ?? default;
        var point = _window.PointFromScreen(screen);

        return point.Y < border.Top
            || point.X < border.Left
            || point.X > _window.ActualWidth - border.Right;
    }

    private static bool Contains(FrameworkElement element, Point screen)
    {
        if (element.IsVisible is false || PresentationSource.FromVisual(element) is null)
        {
            return false;
        }

        var point = element.PointFromScreen(screen);

        return point.X >= 0 && point.Y >= 0 && point.X < element.ActualWidth && point.Y < element.ActualHeight;
    }
}
