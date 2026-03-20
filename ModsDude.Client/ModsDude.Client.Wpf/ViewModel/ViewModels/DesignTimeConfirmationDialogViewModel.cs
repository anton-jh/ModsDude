namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public class DesignTimeConfirmationDialogViewModel()
    : ConfirmationDialogViewModel(
        "Confirm deletion",
        "Are you sure you want to delete xyz?\nThis action cannot be undone!",
        IconKind.Question)
{
}
