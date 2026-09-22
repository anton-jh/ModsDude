using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.View.UserControls;

public partial class SidebarHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(SidebarHeader),
            new PropertyMetadata(""));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }


    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(SidebarHeader),
            new PropertyMetadata(null));

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }


    /// <summary>What the refresh button says it does, so it is not a glyph nobody has to guess at.</summary>
    public static readonly DependencyProperty ActionToolTipProperty =
        DependencyProperty.Register(
            nameof(ActionToolTip),
            typeof(string),
            typeof(SidebarHeader),
            new PropertyMetadata(null));

    public string? ActionToolTip
    {
        get => (string?)GetValue(ActionToolTipProperty);
        set => SetValue(ActionToolTipProperty, value);
    }


    public static readonly DependencyProperty ActionIconProperty =
        DependencyProperty.Register(
            nameof(ActionIcon),
            typeof(string),
            typeof(SidebarHeader),
            new PropertyMetadata(""));

    public string ActionIcon
    {
        get => (string)GetValue(ActionIconProperty);
        set => SetValue(ActionIconProperty, value);
    }


    /// <summary>
    /// Whether the server has changes the list below does not show yet. Drawn as a dot on the refresh
    /// button, and beside the heading in a rail, where the button is not drawn at all; the tooltip is
    /// the owner's to reword, in <see cref="ActionToolTip"/>.
    /// </summary>
    public static readonly DependencyProperty HasPendingChangesProperty =
        DependencyProperty.Register(
            nameof(HasPendingChanges),
            typeof(bool),
            typeof(SidebarHeader),
            new PropertyMetadata(false));

    public bool HasPendingChanges
    {
        get => (bool)GetValue(HasPendingChangesProperty);
        set => SetValue(HasPendingChangesProperty, value);
    }


    /// <summary>
    /// The "create one" action, drawn as a "+" beside the refresh. Null draws no button at all, which
    /// is every header that has nothing to create.
    /// </summary>
    public static readonly DependencyProperty AddCommandProperty =
        DependencyProperty.Register(
            nameof(AddCommand),
            typeof(ICommand),
            typeof(SidebarHeader),
            new PropertyMetadata(null));

    public ICommand? AddCommand
    {
        get => (ICommand?)GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }


    /// <summary>Whether the page the "+" opens is the one showing.</summary>
    public static readonly DependencyProperty IsAddSelectedProperty =
        DependencyProperty.Register(
            nameof(IsAddSelected),
            typeof(bool),
            typeof(SidebarHeader),
            new PropertyMetadata(false));

    public bool IsAddSelected
    {
        get => (bool)GetValue(IsAddSelectedProperty);
        set => SetValue(IsAddSelectedProperty, value);
    }


    /// <summary>Whether this account may use it; the reason it may not is <see cref="AddToolTip"/>.</summary>
    public static readonly DependencyProperty IsAddEnabledProperty =
        DependencyProperty.Register(
            nameof(IsAddEnabled),
            typeof(bool),
            typeof(SidebarHeader),
            new PropertyMetadata(true));

    public bool IsAddEnabled
    {
        get => (bool)GetValue(IsAddEnabledProperty);
        set => SetValue(IsAddEnabledProperty, value);
    }


    public static readonly DependencyProperty AddToolTipProperty =
        DependencyProperty.Register(
            nameof(AddToolTip),
            typeof(string),
            typeof(SidebarHeader),
            new PropertyMetadata(""));

    public string AddToolTip
    {
        get => (string)GetValue(AddToolTipProperty);
        set => SetValue(AddToolTipProperty, value);
    }


    /// <summary>
    /// Never closer to the left edge than this, for a heading too wide to be centred in a rail. A rail cuts
    /// off what does not fit, and a few pixels of margin is what keeps the start of the word readable.
    /// </summary>
    private const double MinInset = 4;

    /// <summary>Used only where there is no shell to ask, as in a designer.</summary>
    private const double FallbackRailWidth = 60;


    public SidebarHeader()
    {
        InitializeComponent();

        Loaded += (_, _) => PositionTitle();
        IsVisibleChanged += (_, _) => PositionTitle();
    }


    /// <summary>
    /// Puts the heading where a rail would centre it, and leaves it there. It is at that place open as
    /// well as collapsed, so opening the sidebar changes nothing about it: the heading is part of the
    /// rail's column, and the column does not move.
    /// </summary>
    /// <remarks>
    /// Worked out from the shell's rail width rather than from this control's own width, because this
    /// control is a full sidebar wide when the sidebar is open.
    /// </remarks>
    private void PositionTitle()
    {
        TitleText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var railWidth = RailWidth();

        TitleShift.X = Math.Max(MinInset, (railWidth - TitleText.DesiredSize.Width) / 2);

        // Just past the end of the word, or as far as the rail allows: a dot the rail cuts off is no dot,
        // so it is pulled back over the last letter's shoulder instead.
        TitleDotShift.X = Math.Min(
            TitleShift.X + TitleText.DesiredSize.Width + 1,
            railWidth - TitleDot.Width - 2);
    }

    private double RailWidth()
    {
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is SidebarShell shell)
            {
                return shell.CollapsedWidth;
            }
        }

        return FallbackRailWidth;
    }
}
