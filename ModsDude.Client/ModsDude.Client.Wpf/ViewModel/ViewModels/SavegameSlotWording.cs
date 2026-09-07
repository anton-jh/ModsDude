using ModsDude.Client.Core.Models;
using System.Text;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// How a slot is described where there is room for all of it. Shared by the slot row and the slot
/// picker, which show the same slot and must not describe it two different ways.
/// </summary>
internal static class SavegameSlotWording
{
    /// <summary>
    /// How many of the adapter's details fit on a row's one line. Its order is its priority order,
    /// which is what makes taking a prefix of it a reasonable thing to do.
    /// </summary>
    public const int DetailsOnTheRow = 3;


    /// <summary>
    /// Everything about a slot, for the tooltip: what the save is called, every detail the adapter
    /// recorded, and - last, and only here - the adapter's own id for the slot, which is for
    /// somebody debugging rather than somebody playing.
    /// </summary>
    public static string DescribeFully(string label, SavegameSlotId id, IReadOnlyList<SavegameDetail> details)
    {
        var text = new StringBuilder(label);

        foreach (var detail in details)
        {
            text.Append('\n').Append(detail.Label).Append(": ").Append(detail.Value);
        }

        return text.Append('\n').Append(id.Value).ToString();
    }
}
