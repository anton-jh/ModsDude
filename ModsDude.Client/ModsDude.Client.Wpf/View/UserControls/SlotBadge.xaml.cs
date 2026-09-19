using System.Windows;
using System.Windows.Controls;

namespace ModsDude.Client.Wpf.View.UserControls;

/// <summary>
/// A slot's number, for a game whose slots are a small numbered set. Collapses itself for a game whose
/// slots are not, so a template can carry one without asking whether it applies.
/// </summary>
public partial class SlotBadge : UserControl
{
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number),
        typeof(int?),
        typeof(SlotBadge),
        new PropertyMetadata(null, (d, _) => ((SlotBadge)d).Refresh()));

    public SlotBadge()
    {
        InitializeComponent();

        Refresh();
    }


    /// <summary>The number, or null where the game does not number its slots.</summary>
    public int? Number
    {
        get => (int?)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }


    private void Refresh()
    {
        Visibility = Number is null ? Visibility.Collapsed : Visibility.Visible;
        Digits.Text = Number?.ToString() ?? string.Empty;
    }
}
