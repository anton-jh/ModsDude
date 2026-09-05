using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Checking a savegame back in: an optional description of what happened, and whether you are done
/// with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It asks nothing about the slot.</b> The open checkout already names it. Choosing between twenty
/// near-identical folders from memory is precisely where a wrong answer publishes somebody else's
/// farm under this save's name and burns a version doing it.
/// </para>
/// <para>
/// <b>Keep playing is the mid-session backup.</b> The same version is minted, but the local copy and
/// the claim both stay - so saving your progress for the others to see does not become an upload
/// followed immediately by downloading what was just sent.
/// </para>
/// <para>
/// The description is never required. A field the button refuses to work without is answered with
/// "asdf" by the third check-in - the same reasoning as the mod editor's version description.
/// </para>
/// </remarks>
public partial class SavegameCheckInModalViewModel : ModalViewModel
{
    public SavegameCheckInModalViewModel(string savegameName, string slotLabel)
    {
        SavegameName = savegameName;
        SlotLabel = slotLabel;
    }


    public string SavegameName { get; }
    public string SlotLabel { get; }

    public string Title => $"Check '{SavegameName}' in";

    public string Message =>
        $"Everything in '{SlotLabel}' is uploaded as a new version, and the others can take it from there. " +
        "A save that changed nothing mints nothing.";

    [ObservableProperty]
    private string _label = "";

    /// <summary>
    /// Mints the version and keeps both the copy and the claim, for somebody who is still playing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel))]
    [NotifyPropertyChangedFor(nameof(Consequence))]
    private bool _keepPlaying;

    /// <summary>True where the user confirmed. The page reads the two fields off this.</summary>
    public bool Result { get; private set; }

    /// <summary>Blank means no description, which is the ordinary answer.</summary>
    public string? TrimmedLabel => string.IsNullOrWhiteSpace(Label) ? null : Label.Trim();

    /// <summary>The verb carries what happens to the copy on this machine, not just "OK".</summary>
    public string ConfirmLabel => KeepPlaying
        ? "Check in and keep playing"
        : "Check in - the local copy goes to the Recycle Bin";

    public string Consequence => KeepPlaying
        ? "The save stays in its slot and stays yours. Nobody else can take it until you check in without this ticked."
        : "The slot is freed once the upload is verified, and the save is anybody's to take.";


    [RelayCommand]
    private void Confirm()
    {
        Result = true;
        Done = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        Done = true;
    }
}
