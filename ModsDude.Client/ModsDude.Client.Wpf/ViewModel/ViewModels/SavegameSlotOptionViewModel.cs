using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One row of the slot picker: a place a savegame can be written, named the way the player names it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never the folder number.</b> A picker that offers "savegame3" is the memory test this whole
/// feature exists to remove, so an occupied slot is labelled with what the <em>game</em> calls the
/// save in it and how long it has been played. The adapter's own id is on the tooltip and nowhere
/// else, for the one person who is debugging rather than playing.
/// </para>
/// <para>
/// <b>The three safety states are told apart here, not at the moment of writing.</b> A refused row is
/// visibly refused and carries the action that unblocks it, rather than being a button that destroys
/// somebody's evening and then apologises.
/// </para>
/// </remarks>
public sealed class SavegameSlotOptionViewModel
{
    public SavegameSlotOptionViewModel(
        SavegameSlot slot,
        SavegameSlotAvailability availability,
        Guid? occupyingSavegameId = null,
        string? occupyingSavegameName = null)
    {
        Id = slot.Id;
        Availability = availability;
        OccupyingSavegameId = occupyingSavegameId;
        OccupyingSavegameName = occupyingSavegameName;

        SaveName = slot.DisplayName;
        PlaytimeText = SavegameWording.Playtime(slot.Playtime);

        Label = slot.IsOccupied
            ? slot.DisplayName is { Length: > 0 } name ? name : "A save this game will not name"
            : "Empty slot";

        Detail = BuildDetail();
        ToolTip = $"{Label}\n{Id.Value}";

        IsRefused = SavegameSlotStates.IsRefused(availability);
        NeedsConfirmation = SavegameSlotStates.RequiresConfirmation(availability);
        IsFree = availability is SavegameSlotAvailability.Free;
    }


    public SavegameSlotId Id { get; }

    public SavegameSlotAvailability Availability { get; }

    /// <summary>What the game calls the save sitting here, or null for an empty slot.</summary>
    public string? SaveName { get; }

    /// <summary>The row's headline - the save's own name, or that the slot is empty.</summary>
    public string Label { get; }

    /// <summary>The second line: playtime, and what ModsDude knows about who put this here.</summary>
    public string Detail { get; }

    public string? PlaytimeText { get; }

    public string ToolTip { get; }

    /// <summary>Which savegame this machine records as checked out here, if any.</summary>
    public Guid? OccupyingSavegameId { get; }

    public string? OccupyingSavegameName { get; }

    /// <summary>Writing here is refused outright - it holds play that exists nowhere else.</summary>
    public bool IsRefused { get; }

    /// <summary>Writing here costs something recoverable, so it is asked about first.</summary>
    public bool NeedsConfirmation { get; }

    public bool IsFree { get; }

    public bool CanBeChosen => IsRefused is false;

    /// <summary>
    /// The single action offered instead of a doomed write - check that savegame in, and this slot
    /// frees itself. Empty where the slot is refused but nothing here can name what holds it, which
    /// is the binding-without-a-server-record case.
    /// </summary>
    public string BlockedAction => OccupyingSavegameName is { Length: > 0 } name
        ? $"Check '{name}' in first"
        : "Check that savegame in first";


    private string BuildDetail()
    {
        var parts = new List<string>();

        if (PlaytimeText is not null)
        {
            parts.Add(PlaytimeText);
        }

        parts.Add(Availability switch
        {
            SavegameSlotAvailability.Free => "Nothing here",
            SavegameSlotAvailability.HeldClean => OccupyingSavegameName is { Length: > 0 } clean
                ? $"'{clean}' is checked out here, exactly as it was downloaded"
                : "A checked-out savegame is here, exactly as it was downloaded",
            SavegameSlotAvailability.HeldWithUnpublishedPlay => OccupyingSavegameName is { Length: > 0 } played
                ? $"'{played}' has been played here and not checked in - this exists nowhere else"
                : "This has been played and not checked in - it exists nowhere else",
            SavegameSlotAvailability.Unrecognised => "Not from this repo. ModsDude has no copy of it",
            _ => "Not from this repo. ModsDude has no copy of it"
        });

        return string.Join(" · ", parts);
    }
}
