using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Shell.Modals;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// Which save already on this disk is about to be published, chosen before the publish step asks
/// anything about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Publishing is inherently about a slot</b>, and this is the step that used to be a button on a
/// row of the game's own slot list. Reaching it from the repo's Saves list means the slot has to be
/// named rather than clicked, so it is asked first and on its own: the publish step's every other
/// question - the name, the mod list, the revision it declares - is about the bytes this one picks.
/// </para>
/// <para>
/// <b>Only slots ModsDude has no copy of are offered.</b> An empty slot has nothing to publish and a
/// checked-out one is checked in rather than published a second time under a new name, so neither is
/// in the list at all - a picker whose rows mostly refuse is worse than a short one.
/// </para>
/// </remarks>
public partial class SavegameSlotPickerStepViewModel : WizardStepViewModel
{
    /// <param name="preselect">The slot to open on, where it is listed. Otherwise the first.</param>
    public SavegameSlotPickerStepViewModel(string repoName, IReadOnlyList<SavegameSlotOptionViewModel> slots, SavegameSlotRef? preselect = null)
    {
        RepoName = repoName;
        Slots = [.. slots];

        // Grouped under a folder heading each only where the game reaches more than one savegame
        // folder, which is what a slot carrying a target name means. The same call both other slot
        // lists make, so the three cannot decide differently about the same slots.
        SlotGrouping.Apply(Slots, slots.Any(x => x.TargetName is not null));

        _selectedSlot = Slots.FirstOrDefault(x => preselect is SavegameSlotRef wanted && x.Ref.Addresses(wanted))
            ?? Slots.FirstOrDefault();

        Choices = [new WizardChoice("Select") { IsDefault = true, EnabledWhen = () => SelectedSlot is not null }];
    }


    public string RepoName { get; }

    public ObservableCollection<SavegameSlotOptionViewModel> Slots { get; }

    public override string Title => "Publish a savegame";

    public string Message =>
        $"Select a slot to publish to {RepoName}. Slots already published are not listed.";


    [ObservableProperty]
    private SavegameSlotOptionViewModel? _selectedSlot;
}
