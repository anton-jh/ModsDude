using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Takes a list of mod names or ids as text - off a forum post, a modpack manifest, a message from
/// whoever runs the server - and hands back the terms it found.
/// </summary>
/// <remarks>
/// <para>
/// <b>It picks; it does not add.</b> What comes back is a selection on the available list, not a
/// changed profile. Somebody else's list is a suggestion, and the step between reading it and
/// committing to it is exactly where a person wants to look at what was matched - which of these are
/// already here, which were not found at all - before anything moves.
/// </para>
/// <para>
/// Matching is done by the page, against ids first and then names, and it is exact. A fuzzy match
/// here would quietly pin the wrong mod, and "3 were not found" is a far better outcome than three
/// plausible mistakes nobody notices until the game does.
/// </para>
/// </remarks>
public partial class PasteModListModalViewModel : ModalViewModel
{
    public string Title => "Paste a mod list";

    public string Message => "One mod per line, or separated by commas. Names or ids both work, and bullets, "
        + "numbering and quotes are ignored.";

    public string Hint => "Matches are selected in the list on the left, ready for you to look over before you add them. "
        + "Nothing is added by this dialog.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _text = string.Empty;

    /// <summary>Counted live, so the parser's reading of the text is visible before it is acted on.</summary>
    public string CountText => Terms.Count switch
    {
        0 => "Nothing to look for yet",
        1 => "1 name",
        var count => $"{count} names"
    };

    /// <summary>Empty until something is confirmed.</summary>
    public IReadOnlyList<string> Result { get; private set; } = [];


    private IReadOnlyList<string> Terms => ModListPaste.Parse(Text);


    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = Terms;
        Done = true;
    }

    private bool CanConfirm() => Terms.Count > 0;

    [RelayCommand]
    private void Cancel()
    {
        Done = true;
    }
}
