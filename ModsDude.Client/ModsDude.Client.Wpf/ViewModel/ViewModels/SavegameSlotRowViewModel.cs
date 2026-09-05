using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One slot of one instance, as the local half of savegames sees it: free, holding something checked
/// out, or holding a save ModsDude has never seen.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is where Publish lives</b>, because publishing is inherently about a slot: it takes bytes
/// that are already on this disk and makes a savegame of them. It asks nothing about the profile - the
/// instance has an active one, and that is what the first version records.
/// </para>
/// <para>
/// <b>Unchecked-in play is called out rather than left to be inferred.</b> A slot whose contents have
/// moved since they were written is the one state on this page that exists nowhere else, and it is the
/// reason the row's own action is Check in rather than anything else.
/// </para>
/// </remarks>
public partial class SavegameSlotRowViewModel : ObservableObject
{
    public SavegameSlotRowViewModel(
        SavegameSlot slot,
        SavegameSlotAvailability availability,
        Guid? savegameId,
        string? savegameName,
        bool canPublish,
        bool canCheckIn)
    {
        Slot = slot;
        Availability = availability;
        SavegameId = savegameId;
        SavegameName = savegameName;

        SaveName = slot.DisplayName;
        PlaytimeText = SavegameWording.Playtime(slot.Playtime);

        Label = slot.IsOccupied
            ? slot.DisplayName is { Length: > 0 } name ? name : "A save this game will not name"
            : "Empty slot";

        IsHeld = availability is SavegameSlotAvailability.HeldClean or SavegameSlotAvailability.HeldWithUnpublishedPlay;
        HasUnpublishedPlay = availability is SavegameSlotAvailability.HeldWithUnpublishedPlay;

        // Publishing needs bytes nobody has claimed. An empty slot has nothing to publish and a
        // checked-out one is checked in rather than published a second time under a new name.
        CanPublish = canPublish && availability is SavegameSlotAvailability.Unrecognised;
        CanCheckIn = canCheckIn && IsHeld && savegameId is not null;
        CanDiscard = CanCheckIn;

        Chip = BuildChip();
        Detail = BuildDetail();
    }


    public event EventHandler? PublishRequested;
    public event EventHandler? CheckInRequested;
    public event EventHandler? DiscardRequested;


    public SavegameSlot Slot { get; }
    public SavegameSlotId Id => Slot.Id;
    public SavegameSlotAvailability Availability { get; }

    /// <summary>The savegame checked out here, where this machine records one.</summary>
    public Guid? SavegameId { get; }
    public string? SavegameName { get; }

    /// <summary>What the game calls the save in this slot. Null where the slot is empty or unreadable.</summary>
    public string? SaveName { get; }

    public string Label { get; }
    public string Detail { get; }
    public string? PlaytimeText { get; }
    public SavegameChip Chip { get; }

    public bool IsHeld { get; }
    public bool HasUnpublishedPlay { get; }

    public bool CanPublish { get; }
    public bool CanCheckIn { get; }
    public bool CanDiscard { get; }

    /// <summary>The one line that says why this slot is worth acting on, for a row that has an action.</summary>
    public string ToolTip => $"{Label}\n{Id.Value}";


    [RelayCommand(CanExecute = nameof(CanPublish))]
    private void Publish() => PublishRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanCheckIn))]
    private void CheckIn() => CheckInRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard() => DiscardRequested?.Invoke(this, EventArgs.Empty);


    private SavegameChip BuildChip() => Availability switch
    {
        SavegameSlotAvailability.Free => new SavegameChip("Free", SavegameChipTone.Neutral),
        SavegameSlotAvailability.HeldClean => new SavegameChip("Checked out to you", SavegameChipTone.Accent),
        SavegameSlotAvailability.HeldWithUnpublishedPlay => new SavegameChip("Played, not checked in", SavegameChipTone.Caution),
        _ => new SavegameChip("Not from this repo", SavegameChipTone.Neutral)
    };

    private string BuildDetail()
    {
        var parts = new List<string>();

        if (PlaytimeText is not null)
        {
            parts.Add(PlaytimeText);
        }

        parts.Add(Availability switch
        {
            SavegameSlotAvailability.Free => "Nothing here. A check-out can write into it without asking.",
            SavegameSlotAvailability.HeldClean => SavegameName is { Length: > 0 } clean
                ? $"'{clean}', exactly as it was downloaded. Checking it in mints nothing until it has been played."
                : "Exactly as it was downloaded. Checking it in mints nothing until it has been played.",
            SavegameSlotAvailability.HeldWithUnpublishedPlay => SavegameName is { Length: > 0 } played
                ? $"'{played}' has been played here. This exists nowhere else until it is checked in."
                : "This has been played here and exists nowhere else until it is checked in.",
            _ => "ModsDude has no copy of this. Publishing it is what puts it in the repo."
        });

        return string.Join(" · ", parts);
    }
}
