using System.Collections;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Groups a slot list under its targets, where the game has more than one of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The key is what groups the picker</b>, and this is the whole of that: both the game's slot
/// list and the check-out dialog show one flat list across every savegame folder a game reaches, and
/// a heading appears over each folder only where there is more than one to tell apart. Shared so the
/// two cannot decide differently - they are showing the same slots.
/// </para>
/// <para>
/// Applied to the default view rather than through a view of our own, because that is the view an
/// <c>ItemsSource</c> bound straight to the collection is already using; the XAML side is a
/// <c>GroupStyle</c> and nothing else.
/// </para>
/// </remarks>
internal static class SlotGrouping
{
    /// <param name="rows">The bound collection. Its default view is what is grouped.</param>
    /// <param name="grouped">
    /// Whether the game reaches more than one savegame folder. False clears any grouping, which is
    /// what a game that has just lost a target needs.
    /// </param>
    public static void Apply(IEnumerable rows, bool grouped)
    {
        var view = CollectionViewSource.GetDefaultView(rows);

        view.GroupDescriptions.Clear();

        if (grouped)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription("TargetName"));
        }
    }
}
