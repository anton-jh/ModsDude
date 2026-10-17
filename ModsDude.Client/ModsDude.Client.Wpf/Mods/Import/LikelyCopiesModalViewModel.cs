using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Shell.Modals;
using System.IO;

namespace ModsDude.Client.Wpf.Mods.Import;

/// <summary>
/// Asks, once per import, about mods whose names look like accidental copies of another mod, such
/// as <c>x (1)</c>. Leaving them out is the default; importing them is for somebody who knows the
/// names are real.
/// </summary>
public partial class LikelyCopiesModalViewModel : ModalViewModel
{
    public LikelyCopiesModalViewModel(IReadOnlyList<CatalogModVersion> versions)
    {
        Files = [.. versions.Select(x => new LikelyCopyItem(FileNameOf(x), x.LikelyCopyOf?.Value ?? string.Empty))];
    }


    public IReadOnlyList<LikelyCopyItem> Files { get; }

    public string Title => Files.Count == 1
        ? "This looks like a copy"
        : $"{Files.Count} files look like copies";

    public string Message => Files.Count == 1
        ? "Windows names a second download or a copy like this. It is probably not the mod's real name."
        : "Windows names a second download or a copy like these. They are probably not the mods' real names.";

    public string LeaveOutText => Files.Count == 1 ? "Leave it out" : "Leave them out";

    public string ImportAnywayHint => Files.Count == 1
        ? "Only if you know this is the mod's real name."
        : "Only if you know these are the mods' real names.";

    /// <summary>Whether to import them anyway. Read after the modal is done.</summary>
    public bool Result { get; private set; }


    [RelayCommand]
    private void LeaveOut()
    {
        Result = false;
        Done = true;
    }

    [RelayCommand]
    private void ImportAnyway()
    {
        Result = true;
        Done = true;
    }


    public override bool TryCancel() => Press(LeaveOutCommand);

    public override bool TryAccept() => Press(LeaveOutCommand);


    private static string FileNameOf(CatalogModVersion version)
        => version.FoundIn.FirstOrDefault() is ModOccurrence occurrence
            ? Path.GetFileName(occurrence.FilePath)
            : version.ModId.Value;
}


/// <param name="Original">The mod it looks like a copy of.</param>
public sealed record LikelyCopyItem(string FileName, string Original);
