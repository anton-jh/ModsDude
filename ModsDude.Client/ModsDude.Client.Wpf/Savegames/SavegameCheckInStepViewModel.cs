using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// Checking a savegame back in: an optional description of what happened, and whether you are done
/// with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It asks nothing about the slot.</b> The open checkout already names it. Choosing between twenty
/// near-identical folders from memory is precisely where a wrong answer publishes somebody else's
/// savegame under this save's name and burns a snapshot doing it.
/// </para>
/// <para>
/// <b>Keep playing is the mid-session backup.</b> The same snapshot is minted, but the local copy and
/// the claim both stay - so saving your progress for the others to see does not become an upload
/// followed immediately by downloading what was just sent. It is not offered where the check-in is
/// a step towards something that needs the save handed back.
/// </para>
/// <para>
/// The description is never required. A field the button refuses to work without is answered with
/// "asdf" by the third check-in - the same reasoning as the mod editor's version description.
/// </para>
/// <para>
/// <b>It names the mod list the play is being attributed to.</b> That attribution is observed rather
/// than declared, so this is the one
/// moment it is visible to the person who would know it was wrong, and the moment before it is
/// recorded for good.
/// </para>
/// </remarks>
public partial class SavegameCheckInStepViewModel : WizardStepViewModel
{
    /// <param name="playedOn">
    /// Which mod list and revision the snapshot being minted will record, or null where it records
    /// none - a savegame following no mod list, and one whose revision nothing on this machine knows.
    /// </param>
    /// <param name="slotNumber">The number the player knows the slot by, for a game that numbers them.</param>
    /// <param name="handBackReason">
    /// Why the save has to be handed back, where this check-in is a step towards something else. Null
    /// where keeping it is a choice.
    /// </param>
    public SavegameCheckInStepViewModel(
        HeldSavegameName savegame,
        string? playedOn = null,
        int? slotNumber = null,
        string? handBackReason = null)
    {
        Savegame = savegame;
        SlotNumber = slotNumber;
        PlayedOn = playedOn;
        HandBackReason = handBackReason;

        Choices = [new WizardChoice(() => ConfirmLabel) { IsDefault = true }];
    }


    public HeldSavegameName Savegame { get; }
    public int? SlotNumber { get; }

    /// <inheritdoc cref="SavegameCheckInStepViewModel(HeldSavegameName, string?, int?, string?)"/>
    public string? PlayedOn { get; }

    public bool HasPlayedOn => PlayedOn is { Length: > 0 };

    /// <inheritdoc cref="SavegameCheckInStepViewModel(HeldSavegameName, string?, int?, string?)"/>
    public string? HandBackReason { get; }

    public bool MustHandBack => HandBackReason is not null;

    public bool CanKeepPlaying => MustHandBack is false;

    public override string Title => $"Check in {Savegame.Quoted}";

    public string Message =>
        $"Uploads {Slot} as a new snapshot.";

    private string Slot => Savegame.Name is string name
        ? SavegameSlotWording.Named(SlotNumber, name)
        : SlotNumber is int number ? $"slot {number}" : "its slot";

    [ObservableProperty]
    private string _label = "";

    /// <summary>
    /// Mints the snapshot and keeps both the copy and the claim, for somebody who is still playing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel))]
    [NotifyPropertyChangedFor(nameof(Consequence))]
    private bool _keepPlaying;

    /// <summary>Blank means no description, which is the ordinary answer.</summary>
    public string? TrimmedLabel => string.IsNullOrWhiteSpace(Label) ? null : Label.Trim();

    public string ConfirmLabel => KeepPlaying
        ? "Check in and keep playing"
        : "Check in";

    public string Consequence => KeepPlaying
        ? "It stays checked out to you."
        : "The local copy goes to the Recycle Bin after the upload.";
}
