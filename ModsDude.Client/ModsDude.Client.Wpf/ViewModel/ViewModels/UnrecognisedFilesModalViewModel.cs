using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.ViewModel.Services;
using System.IO;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>Where files nothing else has a copy of go when an apply takes them out of the mod folder.</summary>
public enum UnrecognisedDestination
{
    RecycleBin,
    Folder
}


/// <summary>
/// The answer to <see cref="UnrecognisedFilesModalViewModel"/>: apply, and if so where the files go.
/// </summary>
/// <param name="Folder">The folder to move them to, or null for the Recycle Bin.</param>
public sealed record UnrecognisedFilesChoice(string? Folder);


/// <summary>
/// The one interruption an apply is always worth: files in the mod folder that the repo does not have,
/// so nothing else has a copy of them - named, with a choice of where they go.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Recycle Bin stays the default</b>, because it is the mechanism people already understand and
/// there is nothing to set up. The folder is for somebody who does not want to go digging through the
/// bin for a mod they will want to hand to a teammate - or whose bin is small enough to have refused
/// a folder of large archives.
/// </para>
/// <para>
/// Either way nothing is deleted. That is the one rule this dialog exists to keep, and the reason
/// picking a folder is offered here rather than somewhere in the settings: the moment it matters is
/// the moment the files are named.
/// </para>
/// </remarks>
public partial class UnrecognisedFilesModalViewModel : ModalViewModel
{
    private const int _namesShown = 10;

    private readonly IDialogService _dialogs;


    /// <param name="names">Every file, in the order the plans list them.</param>
    /// <param name="lastFolder">The folder chosen last time, so a second apply starts where the first ended.</param>
    public UnrecognisedFilesModalViewModel(IReadOnlyList<string> names, IDialogService dialogs, string? lastFolder)
    {
        _dialogs = dialogs;

        Count = names.Count;
        Names = [.. names.Take(_namesShown)];
        MoreText = names.Count > _namesShown ? $"...and {names.Count - _namesShown} more" : null;

        if (string.IsNullOrWhiteSpace(lastFolder) is false)
        {
            _folder = lastFolder;
        }
    }


    public string Title => "These are not in the repo";

    public int Count { get; }

    public string Summary => Count == 1
        ? "1 installed file is not registered in this repo, so nothing else has a copy of it:"
        : $"{Count} installed files are not registered in this repo, so nothing else has a copy of them:";

    public IReadOnlyList<string> Names { get; }

    public string? MoreText { get; }

    public bool HasMore => MoreText is not null;

    /// <summary>What the user chose, or null where they backed out. Read after the dialog is done.</summary>
    public UnrecognisedFilesChoice? Result { get; private set; }


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private UnrecognisedDestination _destination = UnrecognisedDestination.RecycleBin;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _folder = string.Empty;


    /// <summary>
    /// A folder has to be named, and has to be somewhere that can be made: a path nothing could be
    /// moved into would be an apply that fails on the first file, after the confirmation.
    /// </summary>
    private bool CanConfirm()
        => Destination is UnrecognisedDestination.RecycleBin || IsUsableFolder(Folder);

    private static bool IsUsableFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path) is false)
        {
            return false;
        }

        // Existing, or one whose parent is - the move creates the last segment itself.
        return Directory.Exists(path) || Directory.Exists(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)));
    }

    /// <summary>Why the folder cannot be used yet, or null where it can or is not the choice.</summary>
    public string? FolderProblem
        => Destination is UnrecognisedDestination.Folder && IsUsableFolder(Folder) is false
            ? "Pick a folder that exists on this computer."
            : null;

    public bool HasFolderProblem => FolderProblem is not null;

    partial void OnDestinationChanged(UnrecognisedDestination value)
        => NotifyProblemChanged();

    partial void OnFolderChanged(string value)
        => NotifyProblemChanged();

    private void NotifyProblemChanged()
    {
        OnPropertyChanged(nameof(FolderProblem));
        OnPropertyChanged(nameof(HasFolderProblem));
    }


    [RelayCommand]
    private void Browse()
    {
        if (_dialogs.PickFolder(string.IsNullOrWhiteSpace(Folder) ? null : Folder) is string picked)
        {
            Folder = picked;

            // Picking one is choosing it: a Browse button beside a radio that stays on the other
            // answer would be a folder chosen and then not used.
            Destination = UnrecognisedDestination.Folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = new UnrecognisedFilesChoice(
            Destination is UnrecognisedDestination.Folder ? Path.TrimEndingDirectorySeparator(Folder.Trim()) : null);

        Done = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        Done = true;
    }


    public override bool TryCancel() => Press(CancelCommand);

    public override bool TryAccept() => Press(ConfirmCommand);
}
