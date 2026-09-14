using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Picks another profile in this repo to read as a source.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the same act as copying a list</b>, which is why it is not the same dialog. A source only
/// ever <em>offers</em>: it puts that profile's versions on the left, where each of them is still a
/// row somebody has to move. Copying writes into the draft, and its second mode takes things out -
/// a statement about the whole list, which no chip could express.
/// </para>
/// <para>
/// What it is for is the diff: switch the repo chip off and this one on, and the left list is
/// exactly what that profile has and this one does not.
/// </para>
/// </remarks>
public partial class PickProfileSourceModalViewModel : ModalViewModel
{
    public PickProfileSourceModalViewModel(IReadOnlyList<ProfileDto> profiles)
    {
        Profiles = profiles;
        _selected = profiles.FirstOrDefault();
    }


    public IReadOnlyList<ProfileDto> Profiles { get; }

    public string Title => "Read another profile";

    public string Message => "Puts everything that profile pins into the list on the left, at the version and lock it holds. "
        + "Nothing is added to this profile until you move it across.";

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>
    /// Says which of the two reasons the list is empty, because they have different answers: a repo
    /// with one profile needs another one made, and a page already reading them all needs nothing.
    /// </summary>
    public string EmptyText => "There is no other profile here that is not already being read.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private ProfileDto? _selected;

    /// <summary>Null until something is confirmed, so a dismissed dialog adds no source.</summary>
    public ProfileDto? Result { get; private set; }


    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = Selected;
        Done = true;
    }

    private bool CanConfirm() => Selected is not null;

    [RelayCommand]
    private void Cancel()
    {
        Done = true;
    }


    public override bool TryCancel() => Press(CancelCommand);

    /// <summary>Enter takes the picked profile, and is refused where nothing is picked, as the button is.</summary>
    public override bool TryAccept() => Press(ConfirmCommand);
}
