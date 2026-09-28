using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// One of the title bar's minimise, maximise and close buttons.
/// </summary>
/// <remarks>
/// A plain <see cref="Button"/> plus a hover and a pressed state that can be set from outside. The maximise
/// button needs them: it answers Windows' hit test as the maximise button so that hovering it opens the Snap
/// Layouts flyout, which makes it non-client area - and WPF sees no mouse over non-client area, so
/// <c>IsMouseOver</c> and <c>IsPressed</c> never change for it. <see cref="WindowTitleBar"/> sets these from
/// the non-client messages instead.
/// </remarks>
public class CaptionButton : Button
{
    public static readonly DependencyProperty IsHoveredProperty =
        DependencyProperty.Register(
            nameof(IsHovered),
            typeof(bool),
            typeof(CaptionButton),
            new PropertyMetadata(false));

    public bool IsHovered
    {
        get => (bool)GetValue(IsHoveredProperty);
        set => SetValue(IsHoveredProperty, value);
    }


    public static readonly DependencyProperty IsPushedProperty =
        DependencyProperty.Register(
            nameof(IsPushed),
            typeof(bool),
            typeof(CaptionButton),
            new PropertyMetadata(false));

    public bool IsPushed
    {
        get => (bool)GetValue(IsPushedProperty);
        set => SetValue(IsPushedProperty, value);
    }


    /// <summary>Red on hover, the way every Windows close button is.</summary>
    public static readonly DependencyProperty IsCloseProperty =
        DependencyProperty.Register(
            nameof(IsClose),
            typeof(bool),
            typeof(CaptionButton),
            new PropertyMetadata(false));

    public bool IsClose
    {
        get => (bool)GetValue(IsCloseProperty);
        set => SetValue(IsCloseProperty, value);
    }
}
