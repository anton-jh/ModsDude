using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.Shell.Modals;
using System.Runtime.ExceptionServices;

namespace ModsDude.Client.Wpf.Shared;

/// <summary>
/// Asks for a name and saves it, staying open until the save succeeds or the user backs out. An
/// expected refusal, such as a taken name, shows under the field so they can pick another.
/// </summary>
/// <remarks>
/// Open it with <see cref="AskAsync"/>. A save that fails unexpectedly closes the modal and is thrown
/// from there, so it reaches the caller's own error handling rather than a modal nobody awaits.
/// </remarks>
public partial class NameModalViewModel : ModalViewModel
{
    private readonly Func<string, Task<string?>> _save;
    private ExceptionDispatchInfo? _failure;


    /// <param name="confirmLabel">The verb, carrying what it does. Never just "OK".</param>
    /// <param name="save">Saves the trimmed name. Returns null on success, or what to show under the field.</param>
    private NameModalViewModel(string title, string message, string suggested, string confirmLabel, Func<string, Task<string?>> save)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        _name = suggested;
        _save = save;
    }


    public string Title { get; }
    public string Message { get; }

    /// <inheritdoc cref="NameModalViewModel(string, string, string, string, Func{string, Task{string?}})"/>
    public string ConfirmLabel { get; }

    /// <summary>The name saved, or null where the user backed out.</summary>
    public string? Result { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isSaving;


    /// <inheritdoc cref="NameModalViewModel(string, string, string, string, Func{string, Task{string?}})"/>
    /// <returns>The name saved, or null where the user backed out.</returns>
    public static async Task<string?> AskAsync(
        IModalService modalService,
        string title,
        string message,
        string suggested,
        string confirmLabel,
        Func<string, Task<string?>> save)
    {
        var modal = new NameModalViewModel(title, message, suggested, confirmLabel, save);

        await modalService.Show(modal);

        modal._failure?.Throw();

        return modal.Result;
    }


    private bool CanConfirm() => string.IsNullOrWhiteSpace(Name) is false && IsSaving is false;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task Confirm()
    {
        var name = Name.Trim();

        Error = null;
        IsSaving = true;

        try
        {
            Error = await _save(name);
        }
        catch (Exception exception)
        {
            _failure = ExceptionDispatchInfo.Capture(exception);
            Done = true;

            return;
        }
        finally
        {
            IsSaving = false;
        }

        if (Error is null)
        {
            Result = name;
            Done = true;
        }
    }

    private bool CanCancel() => IsSaving is false;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        Result = null;
        Done = true;
    }


    public override bool TryCancel() => Press(CancelCommand);

    public override bool TryAccept() => Press(ConfirmCommand);
}
