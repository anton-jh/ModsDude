using Microsoft.Win32;

namespace ModsDude.Client.Wpf.Shared;

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

    public string? PickImage()
    {
        var openFileDialog = new OpenFileDialog()
        {
            Title = "Pick a picture",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|All files|*.*",
            Multiselect = false
        };

        return openFileDialog.ShowDialog() == true
            ? openFileDialog.FileName
            : null;
    }
}
