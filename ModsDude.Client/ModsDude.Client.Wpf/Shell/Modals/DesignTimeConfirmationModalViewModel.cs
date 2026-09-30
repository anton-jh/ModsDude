using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Shell.Modals;

public class DesignTimeConfirmationModalViewModel()
    : ConfirmationModalViewModel(
        "Confirm deletion",
        "Are you sure you want to delete xyz?\nThis action cannot be undone!",
        IconKind.Question)
{
}
