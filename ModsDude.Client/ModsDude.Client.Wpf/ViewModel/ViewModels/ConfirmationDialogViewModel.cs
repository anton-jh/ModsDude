using CommunityToolkit.Mvvm.Input;

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

    /// <summary>
    /// Something that was refused, and why. Both buttons dismiss: the dialog has two, and there is
    /// nothing here to decide, so making them differ would imply a choice that is not on offer.
    /// </summary>
    public static ConfirmationDialogViewModel Refusal(string title, string message)
    {
        return new ConfirmationDialogViewModel(title, message, IconKind.Warning, "Ok", "Ok");
    }
}
