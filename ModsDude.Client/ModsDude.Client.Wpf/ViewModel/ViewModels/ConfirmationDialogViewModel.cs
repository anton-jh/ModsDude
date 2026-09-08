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


    /// <summary>
    /// Putting something away, which is the only way a repo, profile or savegame ever goes away. It
    /// is reversible, so it asks far less insistently than a delete - the weight belongs on the
    /// permanent one, taken later from the archive.
    /// </summary>
    public static ConfirmationDialogViewModel ConfirmArchive(string name, string what)
    {
        return new ConfirmationDialogViewModel(
            $"Archive this {what}?",
            $"'{name}' leaves the list and gives up its name. Nothing else about it changes, and it can be "
                + "brought back from the Archive at any time.",
            IconKind.Question,
            "Archive it",
            "Keep it here");
    }

    /// <summary>
    /// The permanent one, reached from the archive. This is the dialog that gets to be alarming.
    /// </summary>
    public static ConfirmationDialogViewModel ConfirmDelete(string name)
    {
        return new ConfirmationDialogViewModel(
            "Really?",
            $"Are you sure you want to delete '{name}'.\nThis action cannot be undone!",
            IconKind.Warning,
            "Delete",
            "Keep");
    }

    /// <summary>
    /// The permanent one for a repo, which takes the group's whole mod catalog, every profile with
    /// its history and every savegame with it. Separate from <see cref="ConfirmDelete"/> because
    /// that is a different order of loss from one profile or one savegame, and this dialog is the
    /// last place anybody gets told.
    /// </summary>
    public static ConfirmationDialogViewModel ConfirmDeleteRepo(string name)
    {
        return new ConfirmationDialogViewModel(
            "Really?",
            $"Deleting '{name}' also deletes every mod registered in it, every profile with its whole "
                + "history, and every savegame.\nThis action cannot be undone!",
            IconKind.Warning,
            "Delete it all",
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

    /// <summary>
    /// What happened, once it has. The same shape as <see cref="Refusal"/> and deliberately not the
    /// same icon: reporting a finished piece of housekeeping under a warning teaches people that the
    /// warning means nothing.
    /// </summary>
    public static ConfirmationDialogViewModel Notice(string title, string message)
    {
        return new ConfirmationDialogViewModel(title, message, IconKind.None, "Ok", "Ok");
    }
}
