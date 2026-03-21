using Microsoft.Win32;
using ModsDude.Client.Wpf.ViewModel.Services;

namespace ModsDude.Client.Wpf.View.Services;

public class DialogService : IDialogService
{
    public string? PickFolder(string? hint)
    {
        var openFolderDialog = new OpenFolderDialog()
        {
            Title = "Pick a folder",
            Multiselect = false
        };

        if (hint is not null)
        {
            openFolderDialog.DefaultDirectory = hint;
        }

        if (openFolderDialog.ShowDialog() == true)
        {
            return openFolderDialog.FolderName;
        }
        else
        {
            return null;
        }
    }
}
