using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// What this machine knows about the savegame a slot claims to be holding.
/// </summary>
/// <remarks>
/// The answers are not the same thing, and the row reads differently for each. A live savegame is the
/// ordinary case. An archived one is still perfectly real - archiving deliberately does not release
/// anybody's hold - so it still checks in. A savegame in neither list has been deleted from the repo
/// for good, and every server-side verb on it is going to fail.
/// </remarks>
public enum SavegameBindingStanding
{
    /// <summary>Nothing is checked out here, so the question does not arise.</summary>
    None,

    /// <summary>The savegame is in the repo's list.</summary>
    Live,

    /// <summary>It is in the repo's archive. Still claimable, still checkable-in.</summary>
    Archived,

    /// <summary>
    /// The repo has neither - it was archived and then permanently deleted. The binding outlived the
    /// thing it names.
    /// </summary>
    Gone,

    /// <summary>
    /// The repo could not be asked. <b>Not</b> <see cref="Gone"/>: a failed round trip must never be
    /// read as a deletion, because the row that would produce offers to forget somebody's savegame.
    /// </summary>
    Unknown
}


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
/// <para>
/// <b>Disconnect is on every held row</b>, and is the only action on one whose savegame is
/// <see cref="SavegameBindingStanding.Gone"/>. It is the way out of a binding rather than a way out of
/// a checkout: nothing on disk changes and the server is not told, which is what makes it the only
/// thing that can work when there is no longer a savegame to talk to the server about.
/// </para>
/// </remarks>
public partial class SavegameSlotRowViewModel : ObservableObject
{
    public SavegameSlotRowViewModel(
        SavegameSlot slot,
        SavegameSlotAvailability availability,
        Guid? savegameId,
        string? savegameName,
        SavegameBindingStanding standing,
        bool canPublish,
        bool canCheckIn)
    {
        Slot = slot;
        Availability = availability;
        SavegameId = savegameId;
        SavegameName = savegameName;
        Standing = standing;

        SaveName = slot.DisplayName;
        Details = slot.Details;

        Label = slot.IsOccupied
            ? slot.DisplayName is { Length: > 0 } name ? name : "A save this game will not name"
            : "Empty slot";

        IsHeld = availability is SavegameSlotAvailability.HeldClean or SavegameSlotAvailability.HeldWithUnpublishedPlay;
        HasUnpublishedPlay = availability is SavegameSlotAvailability.HeldWithUnpublishedPlay;

        // The one state where the binding names nothing: the savegame was archived and then deleted,
        // so there is no claim to release and no history to check a version into. Offering either
        // would be offering a round trip that is going to come back 404.
        IsOrphaned = IsHeld && standing is SavegameBindingStanding.Gone;

        // Publishing needs bytes nobody has claimed. An empty slot has nothing to publish and a
        // checked-out one is checked in rather than published a second time under a new name.
        CanPublish = canPublish && availability is SavegameSlotAvailability.Unrecognised;
        CanCheckIn = canCheckIn && IsHeld && savegameId is not null && IsOrphaned is false;
        CanDiscard = CanCheckIn;

        // Not gated on membership: it writes nothing anybody else can see. A guest holding a save is
        // as entitled to stop holding it as anybody, and a repo this user cannot write to is exactly
        // where the server-side verbs are refused and this one still has to work.
        CanDisconnect = IsHeld && savegameId is not null;

        Chip = BuildChip();
        Detail = BuildDetail();
    }


    public event EventHandler? PublishRequested;
    public event EventHandler? CheckInRequested;
    public event EventHandler? DiscardRequested;
    public event EventHandler? DisconnectRequested;


    public SavegameSlot Slot { get; }
    public SavegameSlotId Id => Slot.Id;
    public SavegameSlotAvailability Availability { get; }

    /// <summary>The savegame checked out here, where this machine records one.</summary>
    public Guid? SavegameId { get; }
    public string? SavegameName { get; }

    /// <summary>Whether the repo still has the savegame this slot names, as far as anybody could tell.</summary>
    public SavegameBindingStanding Standing { get; }

    /// <summary>Whether this row is holding a savegame the repo has permanently deleted.</summary>
    public bool IsOrphaned { get; }

    /// <summary>What the game calls the save in this slot. Null where the slot is empty or unreadable.</summary>
    public string? SaveName { get; }

    public string Label { get; }
    public string Detail { get; }

    /// <summary>
    /// What the adapter says about the save here - the map, when it was last played, how long for.
    /// Free-form and in the adapter's own order; see <see cref="SavegameDetail"/>.
    /// </summary>
    public IReadOnlyList<SavegameDetail> Details { get; }

    public bool HasDetails => Details.Count > 0;
    public SavegameChip Chip { get; }

    public bool IsHeld { get; }
    public bool HasUnpublishedPlay { get; }

    public bool CanPublish { get; }
    public bool CanCheckIn { get; }
    public bool CanDiscard { get; }
    public bool CanDisconnect { get; }

    /// <summary>The one line that says why this slot is worth acting on, for a row that has an action.</summary>
    public string ToolTip => $"{Label}\n{Id.Value}";


    [RelayCommand(CanExecute = nameof(CanPublish))]
    private void Publish() => PublishRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanCheckIn))]
    private void CheckIn() => CheckInRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard() => DiscardRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect() => DisconnectRequested?.Invoke(this, EventArgs.Empty);


    private SavegameChip BuildChip()
    {
        // Ahead of the held states, because it contradicts them: a row saying "checked out to you"
        // about a savegame nobody can produce is the report this state exists to stop.
        if (IsOrphaned)
        {
            return new SavegameChip("No longer in the repo", SavegameChipTone.Caution);
        }

        return Availability switch
        {
            SavegameSlotAvailability.Free => new SavegameChip("Free", SavegameChipTone.Neutral),
            SavegameSlotAvailability.HeldClean => new SavegameChip("Checked out to you", SavegameChipTone.Accent),
            SavegameSlotAvailability.HeldWithUnpublishedPlay => new SavegameChip("Played, not checked in", SavegameChipTone.Caution),
            _ => new SavegameChip("Not from this repo", SavegameChipTone.Neutral)
        };
    }

    private string BuildDetail()
    {
        var parts = new List<string>();

        // The adapter's own values lead - "Zielonka - 45 h" is what tells two farms apart - and only
        // the first few, because a row is one line. The adapter's order is its priority order, which
        // is the whole reason it is preserved; the rest are on the tooltip.
        parts.AddRange(Details.Take(SavegameSlotWording.DetailsOnTheRow).Select(x => x.Value));

        parts.Add(Describe());

        return string.Join(" · ", parts);
    }

    private string Describe()
    {
        if (IsOrphaned)
        {
            return SavegameName is { Length: > 0 } named
                ? $"'{named}' has been deleted from the repo, so there is nothing left to check in to. Disconnecting leaves the save where it is."
                : "The savegame this was checked out from has been deleted from the repo. Disconnecting leaves the save where it is.";
        }

        // Worth saying, because an archived savegame is missing from every list the user can see and
        // the row would otherwise look like it was naming something that no longer exists.
        var archived = Standing is SavegameBindingStanding.Archived
            ? " It is in the repo's archive; checking it in still works."
            : "";

        return Availability switch
        {
            SavegameSlotAvailability.Free => "Nothing here. A check-out can write into it without asking.",
            SavegameSlotAvailability.HeldClean => (SavegameName is { Length: > 0 } clean
                ? $"'{clean}', exactly as it was downloaded. Checking it in mints nothing until it has been played."
                : "Exactly as it was downloaded. Checking it in mints nothing until it has been played.") + archived,
            SavegameSlotAvailability.HeldWithUnpublishedPlay => (SavegameName is { Length: > 0 } played
                ? $"'{played}' has been played here. This exists nowhere else until it is checked in."
                : "This has been played here and exists nowhere else until it is checked in.") + archived,
            _ => "ModsDude has no copy of this. Publishing it is what puts it in the repo."
        };
    }
}
