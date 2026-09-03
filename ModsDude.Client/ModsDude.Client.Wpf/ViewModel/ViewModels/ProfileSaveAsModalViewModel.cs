using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Asks what to call the profile a revision is being branched off into.
/// </summary>
/// <remarks>
/// The name is the only thing there is to ask. Which revision is being copied was decided by the row
/// this was opened from, and the copy itself is the same primitive as a restore - an old snapshot
/// materialized as a new revision, pointed at a new profile instead of this one.
/// </remarks>
public partial class ProfileSaveAsModalViewModel : ModalViewModel
{
    public ProfileSaveAsModalViewModel(int revision, string suggestedName)
    {
        Revision = revision;
        Name = suggestedName;
    }


    public int Revision { get; }

    public string Message => $"The new profile starts as a copy of revision {Revision}. Nothing about this profile changes.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    /// <summary>The name to create, or <c>null</c> where the dialog was dismissed.</summary>
    public string? Result { get; private set; }


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
