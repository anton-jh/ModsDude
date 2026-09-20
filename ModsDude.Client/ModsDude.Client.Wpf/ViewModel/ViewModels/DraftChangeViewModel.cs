using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One mod as the editor's review of a draft shows it: the shared list row, and what the draft has
/// done to it.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="ProfileModChangeViewModel"/>, which reports what a <em>saved</em>
/// revision changed. This one is of a draft that can still be taken back, so it names the mod it
/// belongs to for the page's revert - and it carries the import that a save is running on it, which a
/// row of the profile's own list no longer does: what a save is doing is read here.
/// </para>
/// <para>
/// <b>Its group is decided when the row is built and not as the import moves.</b> Rows changing
/// group mid-import would reshuffle the list under the pointer that is watching it, so what could
/// not be imported comes to the top once, when the page rebuilds after the save.
/// </para>
/// </remarks>
public sealed class DraftChangeViewModel
{
    public DraftChangeViewModel(ProfileModChange change, ModListItemViewModel item)
    {
        Item = item;
        Change = change;

        Touch = ProfileModTouches.Of(change);
    }


    public ProfileModChange Change { get; }

    /// <summary>The shared list row, which draws the touch mark and carries the import.</summary>
    public ModListItemViewModel Item { get; }

    public ModKey ModId => Change.ModId;

    public string Name => Change.DisplayName;

    public ProfileModTouch Touch { get; }

    /// <summary>The header of the group this row sits under.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Where that group comes among the others. What was not imported first, then the order a diff reads in.</summary>
    public int GroupRank { get; set; }
}
