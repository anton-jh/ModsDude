using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Asks for a name. One field, whatever is being named.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two uses, and they arrive from opposite directions.</b> Restoring something out of the archive
/// asks because it <em>has</em> to: an archived entity gives up its name the instant it is archived,
/// so any number of archived things may share one - which is what makes the archive a place to put
/// things rather than a place that holds names hostage. The price is that restoring can fail, and
/// this is where it is paid, by the one person who is present and knows what the thing should be
/// called now. Renaming asks because that is the whole of the verb.
/// </para>
/// <para>
/// The wording is the caller's, all of it, because those two read nothing alike - which is also why
/// the button is a parameter rather than a word chosen here. A dialog that says "Restore it" over a
/// rename is one somebody has to read twice to be sure of.
/// </para>
/// </remarks>
public partial class RenameModalViewModel : ModalViewModel
{
    /// <param name="confirmLabel">The verb, carrying what it does. Never just "OK".</param>
    public RenameModalViewModel(string title, string message, string suggested, string confirmLabel)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        _name = suggested;
    }


    public string Title { get; }
    public string Message { get; }

    /// <inheritdoc cref="RenameModalViewModel(string, string, string, string)"/>
    public string ConfirmLabel { get; }

    /// <summary>The name given, or null where the user backed out.</summary>
    public string? Result { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;


    private bool CanConfirm() => string.IsNullOrWhiteSpace(Name) is false;


    [RelayCommand(CanExecute = nameof(CanConfirm))]
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


    public override bool TryCancel() => Press(CancelCommand);

    public override bool TryAccept() => Press(ConfirmCommand);
}
