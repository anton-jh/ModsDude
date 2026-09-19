using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// One row of the slot picker: a place a savegame can be written, named the way the player names it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The save's name leads, and the slot's number stands beside it.</b> A picker that offers only
/// "savegame3" is the memory test this whole feature exists to remove, so an occupied slot is labelled with
/// what the <em>game</em> calls the save in it and how long it has been played - but a game with twenty
/// numbered slots is a game whose players say "slot 3", often before they say what is in it, so where the
/// adapter numbers its slots the number is drawn as a badge in front of the row and is never left to be
/// worked out. The adapter's own id - the folder name - stays on the tooltip, as the last line, for
/// somebody who wants to go and look at the folder.
/// </para>
/// <para>
/// <b>The three safety states are told apart here, not at the moment of writing.</b> A refused row is
/// visibly refused and carries the action that unblocks it, rather than being a button that destroys
/// somebody's evening and then apologises.
/// </para>
/// </remarks>
public sealed class SavegameSlotOptionViewModel : IGroupedSlot
{
    public SavegameSlotOptionViewModel(
        GameSavegameSlot slot,
        SavegameSlotAvailability availability,
        Guid? occupyingSavegameId = null,
        string? occupyingSavegameName = null)
    {
        Ref = slot.Ref;
        TargetName = slot.TargetName;
        Availability = availability;
        OccupyingSavegameId = occupyingSavegameId;
        OccupyingSavegameName = occupyingSavegameName;

        SaveName = slot.DisplayName;
        Details = slot.Details;
        Number = slot.Number;

        Label = slot.IsOccupied
            ? slot.DisplayName is { Length: > 0 } name ? name : "A save this game will not name"
            : "Empty slot";

        Detail = BuildDetail();
        ToolTip = SavegameSlotWording.DescribeFully(Label, Ref, TargetName, Details, Number);

        IsRefused = SavegameSlotStates.IsRefused(availability);
        NeedsConfirmation = SavegameSlotStates.RequiresConfirmation(availability);
        IsFree = availability is SavegameSlotAvailability.Free;
    }


    /// <summary>Which of the game's savegame folders this slot is in, and which slot in it.</summary>
    public SavegameSlotRef Ref { get; }

    public SavegameSlotId Id => Ref.Slot;

    /// <summary>
    /// What to call the folder this slot is in, or null where the game has one. The picker groups on
    /// it, which is the whole of what a target means to somebody choosing where a save goes.
    /// </summary>
    public string? TargetName { get; }

    public SavegameSlotAvailability Availability { get; }

    /// <summary>What the game calls the save sitting here, or null for an empty slot.</summary>
    public string? SaveName { get; }

    /// <summary>The row's headline - the save's own name, or that the slot is empty.</summary>
    public string Label { get; }

    /// <summary>The second line: playtime, and what ModsDude knows about who put this here.</summary>
    public string Detail { get; }

    /// <summary>
    /// What the adapter says about the save here - the map, when it was last played, how long for.
    /// Free-form and in the adapter's own order; see <see cref="SavegameDetail"/>.
    /// </summary>
    public IReadOnlyList<SavegameDetail> Details { get; }

    public bool HasDetails => Details.Count > 0;

    /// <summary>
    /// The number the player knows this slot by, for a game that numbers them - the badge on the row.
    /// Null for one that does not.
    /// </summary>
    public int? Number { get; }

    public bool HasNumber => Number is not null;

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

        // The adapter's own values lead - "Zielonka · 45 h" is what tells two savegames apart - and only
        // the first few, because a row is one line. The adapter's order is its priority order, which
        // is the whole reason it is preserved; the rest are on the tooltip.
        parts.AddRange(Details.Take(SavegameSlotWording.DetailsOnTheRow).Select(x => x.Value));

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
