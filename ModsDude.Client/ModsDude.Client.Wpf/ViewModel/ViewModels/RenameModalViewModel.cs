using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// Asks for a name, for the one moment a name is actually needed: bringing something back out of the
/// archive into a name somebody else has since taken.
/// </summary>
/// <remarks>
/// <b>The clash is deferred to here on purpose.</b> An archived entity gives up its name the instant
/// it is archived, so any number of archived things may share one - which is what makes the archive
/// a place to put things rather than a place that holds names hostage. The price is that restoring
/// can fail, and this is where it is paid, by the one person who is present and knows what the thing
/// should be called now.
/// </remarks>
public partial class RenameModalViewModel : ModalViewModel
{
    public RenameModalViewModel(string title, string message, string suggested)
    {
        Title = title;
        Message = message;
        _name = suggested;
    }


    public string Title { get; }
    public string Message { get; }

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
}
