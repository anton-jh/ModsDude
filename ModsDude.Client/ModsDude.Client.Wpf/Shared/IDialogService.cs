namespace ModsDude.Client.Wpf.Shared;

public interface IDialogService
{
    string? PickFolder(string? hint);

    /// <summary>A picture file, or null where the user cancelled.</summary>
    string? PickImage();
}
