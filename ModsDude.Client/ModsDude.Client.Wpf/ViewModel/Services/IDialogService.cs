namespace ModsDude.Client.Wpf.ViewModel.Services;

public interface IDialogService
{
    string? PickFolder(string? hint);

    /// <summary>A picture file, or null where the user cancelled.</summary>
    string? PickImage();
}
