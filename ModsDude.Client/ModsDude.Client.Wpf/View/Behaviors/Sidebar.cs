using System.Windows;

namespace ModsDude.Client.Wpf.View.Behaviors;

/// <summary>
/// Whether a sidebar is being drawn as a rail - icons only - rather than at full width.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inherited, and read by the things that draw differently</b> rather than pushed into them: the
/// shell sets it once on the sidebar it hosts, and the row template, the group headings, the list
/// headers and the account panel each look at it for themselves. One template then serves both widths,
/// instead of a second copy of every sidebar that has to be kept in step with the first.
/// </para>
/// <para>
/// A peek is drawn at full width - it is the sidebar as it would be if it were open - so the shell
/// leaves it false there even while the docked copy beside it is true.
/// </para>
/// </remarks>
public static class Sidebar
{
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.RegisterAttached(
            "IsCompact",
            typeof(bool),
            typeof(Sidebar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsCompact(DependencyObject element)
        => (bool)element.GetValue(IsCompactProperty);

    public static void SetIsCompact(DependencyObject element, bool value)
        => element.SetValue(IsCompactProperty, value);


    /// <summary>
    /// Marks a control whose click is a navigation, so that clicking it in a peek puts the peek away. Rows
    /// do this by being rows; this is for a button that does the same thing as one, like a "+". A button that
    /// only acts - refresh - leaves the peek alone, since closing on it is the peek getting in the way.
    /// </summary>
    public static readonly DependencyProperty DismissesPeekProperty =
        DependencyProperty.RegisterAttached(
            "DismissesPeek",
            typeof(bool),
            typeof(Sidebar),
            new PropertyMetadata(false));

    public static bool GetDismissesPeek(DependencyObject element)
        => (bool)element.GetValue(DismissesPeekProperty);

    public static void SetDismissesPeek(DependencyObject element, bool value)
        => element.SetValue(DismissesPeekProperty, value);
}
