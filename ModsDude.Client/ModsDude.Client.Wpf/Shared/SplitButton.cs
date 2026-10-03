using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.Shared;

/// <summary>
/// A button with a caret beside it that opens a menu of other ways to do the same thing. The header
/// is the everyday act; the items, usually <c>SubtleMenuButton</c>s, are the rarer variants.
/// </summary>
/// <remarks>
/// The menu closes itself when one of its items is clicked, so no page has to.
/// </remarks>
public class SplitButton : HeaderedItemsControl
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(SplitButton));

    public static readonly DependencyProperty IsAccentProperty = DependencyProperty.Register(
        nameof(IsAccent), typeof(bool), typeof(SplitButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsMenuAvailableProperty = DependencyProperty.Register(
        nameof(IsMenuAvailable), typeof(bool), typeof(SplitButton), new PropertyMetadata(true));

    public static readonly DependencyProperty IsMenuOpenProperty = DependencyProperty.Register(
        nameof(IsMenuOpen), typeof(bool), typeof(SplitButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MenuPlacementProperty = DependencyProperty.Register(
        nameof(MenuPlacement), typeof(PlacementMode), typeof(SplitButton), new PropertyMetadata(PlacementMode.Bottom));

    public static readonly DependencyProperty CaretToolTipProperty = DependencyProperty.Register(
        nameof(CaretToolTip), typeof(object), typeof(SplitButton));


    public SplitButton()
    {
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnItemClick));
    }


    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public bool IsAccent
    {
        get => (bool)GetValue(IsAccentProperty);
        set => SetValue(IsAccentProperty, value);
    }

    /// <summary>Whether the caret is shown. Without it the button is an ordinary one.</summary>
    public bool IsMenuAvailable
    {
        get => (bool)GetValue(IsMenuAvailableProperty);
        set => SetValue(IsMenuAvailableProperty, value);
    }

    public bool IsMenuOpen
    {
        get => (bool)GetValue(IsMenuOpenProperty);
        set => SetValue(IsMenuOpenProperty, value);
    }

    public PlacementMode MenuPlacement
    {
        get => (PlacementMode)GetValue(MenuPlacementProperty);
        set => SetValue(MenuPlacementProperty, value);
    }

    public object? CaretToolTip
    {
        get => GetValue(CaretToolTipProperty);
        set => SetValue(CaretToolTipProperty, value);
    }


    protected override bool IsItemItsOwnContainerOverride(object item) => item is UIElement;

    /// <summary>
    /// A click from the menu ran its item's command, and the caret has no other way to know it did.
    /// The header and the caret are buttons too, and are told apart by not being items.
    /// </summary>
    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControlFromItemContainer(source) == this)
        {
            IsMenuOpen = false;
        }
    }
}
