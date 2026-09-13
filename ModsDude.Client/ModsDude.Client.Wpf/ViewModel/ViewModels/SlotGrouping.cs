using System.Collections;
using System.Windows.Data;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// A row in a slot list, as the grouping sees it.
/// </summary>
/// <remarks>
/// Two lines of interface for one reason: <see cref="PropertyGroupDescription"/> takes a property
/// name as a string, and a string that stops matching groups everything under one silent null
/// heading rather than failing. Both pickers implement this, so <c>nameof</c> has something to
/// check the name against.
/// </remarks>
internal interface IGroupedSlot
{
    /// <summary>What to call the folder this slot is in, or null where the game reaches one.</summary>
    string? TargetName { get; }
}


/// <summary>
/// Groups a slot list under its targets, where the game has more than one of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The key is what groups a picker</b>, and this is the whole of that: the check-out dialog and
/// the publish dialog each show one flat list across every savegame folder a game reaches, and a
/// heading appears over each folder only where there is more than one to tell apart. Shared so the
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
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IGroupedSlot.TargetName)));
        }
    }
}
