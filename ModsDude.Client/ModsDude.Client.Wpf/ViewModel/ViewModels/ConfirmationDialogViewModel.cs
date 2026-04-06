using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Exceptions;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public partial class ConfirmationDialogViewModel(
    string title,
    string message,
    IconKind icon,
    string yesText = "Yes",
    string noText = "No")
    : ModalViewModel
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public IconKind Icon { get; } = icon;
    public string YesText { get; } = yesText;
    public string NoText { get; } = noText;

    public bool Result { get; private set; }


    [RelayCommand]
    public void SetYes()
    {
        Result = true;
        Done = true;
    }

    [RelayCommand]
    public void SetNo()
    {
        Result = false;
        Done = true;
    }


    public static ConfirmationDialogViewModel ConfirmDelete(string name)
    {
        return new ConfirmationDialogViewModel(
            "Really?",
            $"Are you sure you want to delete '{name}'.\nThis action cannot be undone!",
            IconKind.Warning,
            "Delete",
            "Keep");
    }

    public static ConfirmationDialogViewModel ValidationErrors(List<string> validationErrors)
    {
        string message;

        if (validationErrors.Count > 5)
        {
            message = string.Join('\n', validationErrors[..4]) + "\n...";
        }
        else
        {
            message = string.Join('\n', validationErrors.Take(5));
        }

        return new ConfirmationDialogViewModel(
            "Not so fast!",
            message,
            IconKind.Error,
            "Ok",
            "Sure");
    }

    public static ConfirmationDialogViewModel Error(UserFriendlyException exception)
    {
        var message = $"{exception.Message}.\n\nThis might not help:\n\n{exception.DeveloperMessage}";

        return new ConfirmationDialogViewModel(
            "Oops!",
            message,
            IconKind.Error,
            "Disapointment: immeasurable",
            "Day: ruined");
    }
}
