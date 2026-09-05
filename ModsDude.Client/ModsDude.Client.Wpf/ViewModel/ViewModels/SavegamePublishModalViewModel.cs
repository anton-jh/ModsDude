using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Publishing a save that is already on this machine: what the repo should call it, and optionally
/// what this first version was.
/// </summary>
/// <remarks>
/// <para>
/// <b>Publish is not check-in.</b> "Upload this new thing" and "upload a new version of that thing"
/// have opposite failure modes, and one button doing both is how somebody's farm ends up as a version
/// of somebody else's. This one is only ever reached from a slot, and it names the thing being made.
/// </para>
/// <para>
/// <b>It asks nothing about the profile.</b> The instance has an active one, and that is what the
/// first version records. Asking would be offering a choice whose wrong answers are all worse than
/// the default.
/// </para>
/// </remarks>
public partial class SavegamePublishModalViewModel : ModalViewModel
{
    public SavegamePublishModalViewModel(string slotLabel, string repoName, string profileName, string suggestedName)
    {
        SlotLabel = slotLabel;
        RepoName = repoName;
        ProfileName = profileName;
        _name = suggestedName;
    }


    public string SlotLabel { get; }
    public string RepoName { get; }
    public string ProfileName { get; }

    public string Title => "Publish this save";

    public string Message =>
        $"'{SlotLabel}' is uploaded to {RepoName} as a savegame of its own, recorded against " +
        $"'{ProfileName}' and the mod list this instance is on. The save stays exactly where it is - " +
        "it is yours, checked out, until you check it in.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty]
    private string _label = "";

    /// <summary>The name to publish under, or null where the dialog was dismissed.</summary>
    public string? Result { get; private set; }

    /// <summary>Blank means no description of the first version, which is the ordinary answer.</summary>
    public string? TrimmedLabel => string.IsNullOrWhiteSpace(Label) ? null : Label.Trim();

    /// <summary>
    /// The name is the one thing that has to be answered: it is unique in the repo and it is what
    /// everybody else will see this save called.
    /// </summary>
    public bool IsValid => string.IsNullOrWhiteSpace(Name) is false;


    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        Result = Name.Trim();
        Done = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        Done = true;
    }
}
