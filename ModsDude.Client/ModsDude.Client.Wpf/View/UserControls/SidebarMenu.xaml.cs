using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ModsDude.Client.Wpf.View.UserControls;

public partial class SidebarMenu : UserControl
{
    /// <summary>The entries under Refresh, drawn with their own titles, icons and availability.</summary>
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(IEnumerable<MenuItemViewModel>),
            typeof(SidebarMenu),
            new PropertyMetadata(null));

    public IEnumerable<MenuItemViewModel>? Items
    {
        get => (IEnumerable<MenuItemViewModel>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }


    /// <summary>Run with the entry chosen.</summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(SidebarMenu),
            new PropertyMetadata(null));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }


    /// <summary>Whether the page showing is one of the entries'.</summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected),
            typeof(bool),
            typeof(SidebarMenu),
            new PropertyMetadata(false));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }


    public static readonly DependencyProperty MenuToolTipProperty =
        DependencyProperty.Register(
            nameof(MenuToolTip),
            typeof(string),
            typeof(SidebarMenu),
            new PropertyMetadata("More"));

    public string MenuToolTip
    {
        get => (string)GetValue(MenuToolTipProperty);
        set => SetValue(MenuToolTipProperty, value);
    }


    /// <summary>What the menu's first entry, Refresh, runs.</summary>
    public static readonly DependencyProperty RefreshCommandProperty =
        DependencyProperty.Register(
            nameof(RefreshCommand),
            typeof(ICommand),
            typeof(SidebarMenu),
            new PropertyMetadata(null));

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }


    /// <summary>
    /// Whether the server has changes the list does not show yet. Drawn as a dot on the "⋯" - in a rail as
    /// well, since the button is all a rail shows of it - and again on the Refresh entry.
    /// </summary>
    public static readonly DependencyProperty HasPendingChangesProperty =
        DependencyProperty.Register(
            nameof(HasPendingChanges),
            typeof(bool),
            typeof(SidebarMenu),
            new PropertyMetadata(false));

    public bool HasPendingChanges
    {
        get => (bool)GetValue(HasPendingChangesProperty);
        set => SetValue(HasPendingChangesProperty, value);
    }


    /// <summary>What refreshing would bring in, under the Refresh entry. Null while there is nothing.</summary>
    public static readonly DependencyProperty PendingChangesTextProperty =
        DependencyProperty.Register(
            nameof(PendingChangesText),
            typeof(string),
            typeof(SidebarMenu),
            new PropertyMetadata(null));

    public string? PendingChangesText
    {
        get => (string?)GetValue(PendingChangesTextProperty);
        set => SetValue(PendingChangesTextProperty, value);
    }


    /// <summary>Used only where there is no shell to ask, as in a designer.</summary>
    private const double FallbackRailWidth = 60;


    public SidebarMenu()
    {
        InitializeComponent();

        MenuPanel.DataContext = this;

        Loaded += (_, _) => Column.Width = RailWidth();
    }


    /// <summary>Runs the chosen entry - Refresh, or one of <see cref="Items"/> - and puts the menu away.</summary>
    private void OnEntryClick(object sender, RoutedEventArgs e)
    {
        MenuButton.IsChecked = false;

        if (ReferenceEquals(e.OriginalSource, RefreshEntry))
        {
            if (RefreshCommand is { } refresh && refresh.CanExecute(null))
            {
                refresh.Execute(null);
            }

            return;
        }

        if (e.OriginalSource is FrameworkElement { DataContext: MenuItemViewModel entry }
            && Command is { } command
            && command.CanExecute(entry))
        {
            command.Execute(entry);
        }
    }

    /// <summary>
    /// The rail's width, which is the column the rows' pictures are centred in. Asked of the shell rather
    /// than read off this control, which is a full sidebar wide when the sidebar is open.
    /// </summary>
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
