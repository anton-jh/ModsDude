using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Which save already on this disk is about to be published, chosen before the publish dialog asks
/// anything about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Publishing is inherently about a slot</b>, and this is the step that used to be a button on a
/// row of the game's own slot list. Reaching it from the repo's Saves list means the slot has to be
/// named rather than clicked, so it is asked first and on its own: the publish dialog's every other
/// question - the name, the mod list, the revision it declares - is about the bytes this one picks.
/// </para>
/// <para>
/// <b>Only slots ModsDude has no copy of are offered.</b> An empty slot has nothing to publish and a
/// checked-out one is checked in rather than published a second time under a new name, so neither is
/// in the list at all - a picker whose rows mostly refuse is worse than a short one.
/// </para>
/// </remarks>
public partial class SavegameSlotPickerModalViewModel : ModalViewModel
{
    public SavegameSlotPickerModalViewModel(string repoName, IReadOnlyList<SavegameSlotOptionViewModel> slots)
    {
        RepoName = repoName;
        Slots = [.. slots];

        // Grouped under a folder heading each only where the game reaches more than one savegame
        // folder, which is what a slot carrying a target name means. The same call both other slot
        // lists make, so the three cannot decide differently about the same slots.
        SlotGrouping.Apply(Slots, slots.Any(x => x.TargetName is not null));

        _selectedSlot = Slots.FirstOrDefault();
    }


    public string RepoName { get; }

    public ObservableCollection<SavegameSlotOptionViewModel> Slots { get; }

    public string Title => "Publish a save";

    public string Message =>
        $"A save that is already on this disk becomes a savegame of its own in {RepoName}. Only saves " +
        "ModsDude has no copy of are listed - a checked-out one is checked in rather than published again.";

    /// <summary>The slot to publish, or null where the dialog was dismissed.</summary>
    public SavegameSlotOptionViewModel? Result { get; private set; }


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private SavegameSlotOptionViewModel? _selectedSlot;


    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = SelectedSlot;
        Done = true;
    }

    private bool CanConfirm() => SelectedSlot is not null;

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        Done = true;
    }
}
