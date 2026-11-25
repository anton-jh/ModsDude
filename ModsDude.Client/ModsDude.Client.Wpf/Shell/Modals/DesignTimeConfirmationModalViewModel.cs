using ModsDude.Client.Wpf.Shell.Sidebar;

namespace ModsDude.Client.Wpf.Shell.Modals;

public class DesignTimeConfirmationModalViewModel()
    : ConfirmationModalViewModel(
        "Really?",
        "'xyz' is deleted permanently.",
        IconKind.Warning,
        "Delete",
        "Cancel")
{
}
