using ModsDude.Client.Wpf.View.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// A sidebar and the page beside it, where the sidebar can shrink to a rail and be peeked at in full
/// without taking the space back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two copies of the sidebar, one template.</b> The docked one is what takes up the column and is
/// compact while <see cref="IsCollapsed"/>; the peek is a second instance drawn over the top of the
/// page, at full width, and takes no room of its own. Both are made from
/// <see cref="SidebarTemplate"/> against the same data context, so they cannot say different things.
/// </para>
/// <para>
/// <b>The peek is in the same grid as the page, above it.</b> That is what makes the stacking work
/// without anything to arrange it: this shell's peek is over the page it hosts, which includes any
/// shell inside that page, so the outer sidebar's peek is always above the inner one's.
/// </para>
/// <para>
/// <b>Two delays, not one.</b> Opening waits a moment so that a pointer passing over the rail on its
/// way somewhere else does not throw a panel open; closing waits longer, so that a pointer that
/// wobbles off the edge while travelling down the list is not punished for it.
/// </para>
/// </remarks>
[TemplatePart(Name = DockedPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PeekPartName, Type = typeof(FrameworkElement))]
public class SidebarShell : ContentControl
{
    private const string DockedPartName = "PART_Docked";
    private const string PeekPartName = "PART_Peek";
    private const string PeekContentPartName = "PART_PeekContent";

    /// <summary>
    /// Long enough to be seen as a movement and not a jump, and eased at both ends: a panel that starts and
    /// stops at full speed reads as a cut. Closing is a little quicker than opening, because by then the
    /// user has made their choice and is waiting to get on.
    /// </summary>
    private static readonly Duration OpenDuration = new(TimeSpan.FromMilliseconds(300));
    private static readonly Duration CloseDuration = new(TimeSpan.FromMilliseconds(240));

    /// <summary>
    /// Long enough to read a label before deciding to open: a rail is something to move across on the way
    /// to a page far more often than it is something to look at.
    /// </summary>
    private readonly DispatcherTimer _openTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <summary>
    /// Whether the pointer is on the docked sidebar. Tracked from its own enter and leave rather than read from
    /// <c>IsMouseOver</c> when needed, because the moment it is needed - the sidebar being collapsed under
    /// a click - is the moment the answer is about to change.
    /// </summary>
    private bool _pointerOnDocked;

    private FrameworkElement? _docked;
    private FrameworkElement? _peek;
    private FrameworkElement? _peekContent;


    public SidebarShell()
    {
        _openTimer.Tick += OnOpenTimerTick;
        _closeTimer.Tick += OnCloseTimerTick;

        Unloaded += OnUnloaded;
    }


    /// <summary>What the sidebar is drawn from. Its data context is this control's.</summary>
    public static readonly DependencyProperty SidebarTemplateProperty =
        DependencyProperty.Register(
            nameof(SidebarTemplate),
            typeof(DataTemplate),
            typeof(SidebarShell),
            new PropertyMetadata(null));

    public DataTemplate? SidebarTemplate
    {
        get => (DataTemplate?)GetValue(SidebarTemplateProperty);
        set => SetValue(SidebarTemplateProperty, value);
    }


    /// <summary>Whether the docked sidebar is a rail. The peek is a full sidebar either way.</summary>
    public static readonly DependencyProperty IsCollapsedProperty =
        DependencyProperty.Register(
            nameof(IsCollapsed),
            typeof(bool),
            typeof(SidebarShell),
            new PropertyMetadata(false, (d, e) => ((SidebarShell)d).OnCollapsedChanged((bool)e.OldValue, (bool)e.NewValue)));

    public bool IsCollapsed
    {
        get => (bool)GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }


    public static readonly DependencyProperty ExpandedWidthProperty =
        DependencyProperty.Register(
            nameof(ExpandedWidth),
            typeof(double),
            typeof(SidebarShell),
            new PropertyMetadata(200.0, (d, _) => ((SidebarShell)d).ApplyState()));

    public double ExpandedWidth
    {
        get => (double)GetValue(ExpandedWidthProperty);
        set => SetValue(ExpandedWidthProperty, value);
    }


    /// <summary>
    /// Wide enough for a 28px tile with room around it and for a short heading under it, and no wider: this
    /// is the space every collapsed sidebar takes out of the page. The icon column in the row template
    /// (<c>SidebarIconColumn</c>, in App.xaml) is this less what the list takes off either side.
    /// </summary>
    public static readonly DependencyProperty CollapsedWidthProperty =
        DependencyProperty.Register(
            nameof(CollapsedWidth),
            typeof(double),
            typeof(SidebarShell),
            new PropertyMetadata(60.0, (d, _) => ((SidebarShell)d).ApplyState()));

    public double CollapsedWidth
    {
        get => (double)GetValue(CollapsedWidthProperty);
        set => SetValue(CollapsedWidthProperty, value);
    }


    /// <summary>
    /// Whether the full-width copy is meant to be showing over the page: true from the moment it is asked
    /// for to the moment it is put away, whatever it is still animating.
    /// </summary>
    public static readonly DependencyProperty IsPeekingProperty =
        DependencyProperty.Register(
            nameof(IsPeeking),
            typeof(bool),
            typeof(SidebarShell),
            new PropertyMetadata(false));

    public bool IsPeeking
    {
        get => (bool)GetValue(IsPeekingProperty);
        private set => SetValue(IsPeekingProperty, value);
    }

    /// <summary>
    /// Whether the copy is on screen at all. It outlasts <see cref="IsPeeking"/> by as long as the closing
    /// animation takes, which is why the template reads this one and the timers read the other.
    /// </summary>
    public static readonly DependencyProperty IsPeekVisibleProperty =
        DependencyProperty.Register(
            nameof(IsPeekVisible),
            typeof(bool),
            typeof(SidebarShell),
            new PropertyMetadata(false));

    public bool IsPeekVisible
    {
        get => (bool)GetValue(IsPeekVisibleProperty);
        private set => SetValue(IsPeekVisibleProperty, value);
    }


    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        Detach();

        _docked = GetTemplateChild(DockedPartName) as FrameworkElement;
        _peek = GetTemplateChild(PeekPartName) as FrameworkElement;
        _peekContent = GetTemplateChild(PeekContentPartName) as FrameworkElement;

        if (_docked is not null)
        {
            _docked.MouseEnter += OnDockedMouseEnter;
            _docked.MouseLeave += OnDockedMouseLeave;
            _docked.IsKeyboardFocusWithinChanged += OnDockedFocusWithinChanged;
        }

        if (_peek is not null)
        {
            _peek.MouseEnter += OnPeekMouseEnter;
            _peek.MouseLeave += OnPeekMouseLeave;
        }

        ApplyState();
    }


    private void Detach()
    {
        if (_docked is not null)
        {
            _docked.MouseEnter -= OnDockedMouseEnter;
            _docked.MouseLeave -= OnDockedMouseLeave;
            _docked.IsKeyboardFocusWithinChanged -= OnDockedFocusWithinChanged;
        }

        if (_peek is not null)
        {
            _peek.MouseEnter -= OnPeekMouseEnter;
            _peek.MouseLeave -= OnPeekMouseLeave;
        }
    }

    /// <summary>
    /// The sidebar is being collapsed - by the page changing under it, which is nearly always a click in it.
    /// If the pointer is on it, it is left open as a peek instead of closing under the pointer, and goes the
    /// way any peek does: when the pointer leaves it.
    /// </summary>
    /// <remarks>
    /// The peek is already at full width and needs no animation - it takes over from the docked sidebar in
    /// the place it was, and the docked one shrinks to a rail underneath. The close timer is started at once
    /// and stopped again by the pointer being found on the peek, so a pointer that has in fact gone still
    /// closes it.
    /// </remarks>
    private void OnCollapsedChanged(bool wasCollapsed, bool isCollapsed)
    {
        var pointerOnSidebar = wasCollapsed is false && isCollapsed && _pointerOnDocked;

        ApplyState();

        if (pointerOnSidebar && _peek is not null)
        {
            _peek.BeginAnimation(WidthProperty, null);
            IsPeeking = true;
            IsPeekVisible = true;

            StartClose();
        }
    }

    /// <summary>
    /// Puts the docked sidebar at the width and in the mode its state calls for, and puts the peek away:
    /// a sidebar that has just been opened has nothing left to peek at, and one that has just been closed
    /// should not still be showing a panel from before.
    /// </summary>
    private void ApplyState()
    {
        if (_docked is not null)
        {
            _docked.Width = IsCollapsed ? CollapsedWidth : ExpandedWidth;
            Sidebar.SetIsCompact(_docked, IsCollapsed);
        }

        if (_peek is not null)
        {
            _peek.Width = ExpandedWidth;
            Sidebar.SetIsCompact(_peek, false);
        }

        if (_peekContent is not null)
        {
            _peekContent.Width = ExpandedWidth;
        }

        HidePeek(animate: false);
    }

    private void ShowPeek()
    {
        _openTimer.Stop();
        _closeTimer.Stop();

        if (IsCollapsed && IsPeeking is false)
        {
            IsPeeking = true;

            // Grown out of the rail's own edge, from where it is now - the rail's width, or wherever a
            // closing animation had got to, so that changing its mind mid-close does not jump. Stopped
            // rather than held, so the width it settles at is the one ApplyState set.
            var from = IsPeekVisible && _peek is { ActualWidth: > 0 } ? _peek.ActualWidth : CollapsedWidth;

            IsPeekVisible = true;

            _peek?.BeginAnimation(
                WidthProperty,
                new DoubleAnimation(from, ExpandedWidth, OpenDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                    FillBehavior = FillBehavior.Stop
                });
        }
    }

    /// <param name="animate">
    /// False where the state has changed under the peek and it has nothing left to close back into.
    /// </param>
    private void HidePeek(bool animate = true)
    {
        _openTimer.Stop();
        _closeTimer.Stop();

        IsPeeking = false;

        if (animate && IsPeekVisible && _peek is { ActualWidth: > 0 } peek)
        {
            // Back into the rail's own edge, and only then taken off the screen.
            var closing = new DoubleAnimation(peek.ActualWidth, CollapsedWidth, CloseDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            closing.Completed += (_, _) =>
            {
                // Reopened in the meantime: the new animation owns the panel now.
                if (IsPeeking is false)
                {
                    _peek?.BeginAnimation(WidthProperty, null);
                    IsPeekVisible = false;
                }
            };

            peek.BeginAnimation(WidthProperty, closing);
        }
        else
        {
            _peek?.BeginAnimation(WidthProperty, null);
            IsPeekVisible = false;
        }
    }

    private void StartOpen()
    {
        if (IsCollapsed is false || IsPeeking)
        {
            return;
        }

        _closeTimer.Stop();
        _openTimer.Stop();
        _openTimer.Start();
    }

    private void StartClose()
    {
        _openTimer.Stop();

        if (IsPeeking is false)
        {
            return;
        }

        _closeTimer.Stop();
        _closeTimer.Start();
    }


    private void OnOpenTimerTick(object? sender, EventArgs e) => ShowPeek();

    private void OnCloseTimerTick(object? sender, EventArgs e) => HidePeek();

    private void OnDockedMouseEnter(object sender, MouseEventArgs e)
    {
        _pointerOnDocked = true;
        _closeTimer.Stop();
        StartOpen();
    }

    private void OnDockedMouseLeave(object sender, MouseEventArgs e)
    {
        _pointerOnDocked = false;
        _openTimer.Stop();

        if (IsPeeking)
        {
            StartClose();
        }
    }

    private void OnPeekMouseEnter(object sender, MouseEventArgs e)
    {
        _closeTimer.Stop();
    }

    private void OnPeekMouseLeave(object sender, MouseEventArgs e)
    {
        StartClose();
    }

    /// <summary>
    /// Focus arriving in the rail by keyboard opens the peek at once, since somebody tabbing in has no
    /// pointer to wait on, and focus leaving it closes it the same way the pointer does. Only keyboard
    /// focus: a click on a tile also focuses the rail, and opening a panel over the page on every click
    /// would be the peek getting in the way of the thing it is a peek at.
    /// </summary>
    private void OnDockedFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            if (InputManager.Current.MostRecentInputDevice is KeyboardDevice)
            {
                ShowPeek();
            }
        }
        else if (_peek is { IsMouseOver: false })
        {
            StartClose();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _openTimer.Stop();
        _closeTimer.Stop();
    }
}
