using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.Shell.Sidebar;

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


    /// <summary>
    /// The "create one" action, drawn as a "+" beside the title. Null draws no button at all, which
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


    public SidebarHeader()
    {
        InitializeComponent();
    }
}
