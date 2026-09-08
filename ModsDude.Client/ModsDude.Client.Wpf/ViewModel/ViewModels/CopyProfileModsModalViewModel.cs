using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>What copying another profile's list does to the one being edited.</summary>
public enum CopyProfileModsMode
{
    /// <summary>Takes what this profile does not already hold and leaves the rest alone.</summary>
    Add,

    /// <summary>Makes this profile's list equal to the other one's.</summary>
    Replace
}

/// <summary>
/// Picks another profile in this repo to take a mod list from.
/// </summary>
/// <remarks>
/// <para>
/// The fastest way to build a profile is almost never to pick its mods one at a time: it is to start
/// from the profile next to it and change what differs. This is that, and it is a draft like every
/// other change on the page - nothing is written until Save, so <em>Replace</em> is recoverable by
/// discarding, by the undo the page offers, or simply by not saving.
/// </para>
/// <para>
/// <b>Adding is the default and replacing is a deliberate second choice.</b> The two read almost the
/// same in a sentence and are very different in effect, so the destructive one is never the one a
/// hurried click lands on.
/// </para>
/// </remarks>
public partial class CopyProfileModsModalViewModel : ModalViewModel
{
    public CopyProfileModsModalViewModel(IReadOnlyList<ProfileDto> profiles, string targetName)
    {
        Profiles = profiles;
        TargetName = targetName;
        _selected = profiles.FirstOrDefault();
    }


    public IReadOnlyList<ProfileDto> Profiles { get; }

    /// <summary>The profile being edited, named so the dialog says which way the mods travel.</summary>
    public string TargetName { get; }

    public string Title => "Copy a mod list";

    public string Message => $"Takes the mod list from another profile in this repo and brings it into {TargetName}. "
        + "Nothing is written until you save.";

    public bool HasProfiles => Profiles.Count > 0;

    public string EmptyText => "This repo has no other profile to copy from.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private ProfileDto? _selected;

    [ObservableProperty]
    private CopyProfileModsMode _mode = CopyProfileModsMode.Add;

    /// <summary>Null until something is confirmed, so a dismissed dialog copies nothing.</summary>
    public ProfileDto? Result { get; private set; }

    public CopyProfileModsMode ResultMode { get; private set; }


    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = Selected;
        ResultMode = Mode;
        Done = true;
    }

    private bool CanConfirm() => Selected is not null;

    [RelayCommand]
    private void Cancel()
    {
        Done = true;
    }
}
