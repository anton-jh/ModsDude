using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Shell.Modals;

namespace ModsDude.Client.Wpf.Profiles.Editor;

/// <summary>
/// Asks, once per import, what to do about downloads that held something other than the version
/// picked: import the version the file holds instead, or leave the mod out.
/// </summary>
/// <remarks>
/// Stopping answers nothing, which stops the save. Anything already registered stays registered.
/// </remarks>
public partial class ModDownloadMismatchModalViewModel : ModalViewModel
{
    public ModDownloadMismatchModalViewModel(IReadOnlyList<ModDownloadMismatch> mismatches)
    {
        Mods = [.. mismatches.Select(x => new ModDownloadMismatchItemViewModel(x))];
    }


    public IReadOnlyList<ModDownloadMismatchItemViewModel> Mods { get; }

    public string Title => Mods.Count == 1
        ? "This download is not the version picked"
        : $"{Mods.Count} downloads are not the versions picked";

    public string Message => "Use what was downloaded instead, or leave the mod as it was.";

    /// <summary>What to do per offered version, or null where the user stopped.</summary>
    public IReadOnlyDictionary<ModVersionIdentity, ModDownloadMismatchAnswer>? Result { get; private set; }


    [RelayCommand]
    private void Confirm()
    {
        Result = Mods.ToDictionary(
            x => x.Identity,
            x => x.UseHeld ? ModDownloadMismatchAnswer.UseHeld : ModDownloadMismatchAnswer.LeaveOut);

        Done = true;
    }

    [RelayCommand]
    private void Stop()
    {
        Result = null;
        Done = true;
    }


    public override bool TryCancel() => Press(StopCommand);

    public override bool TryAccept() => Press(ConfirmCommand);
}


/// <summary>One download that held something else, and the choice about it.</summary>
public partial class ModDownloadMismatchItemViewModel : ObservableObject
{
    public ModDownloadMismatchItemViewModel(ModDownloadMismatch mismatch)
    {
        Identity = mismatch.Offered.Identity;
        Name = mismatch.Offered.Name;
        Picked = mismatch.Offered.VersionId.Value;
        Source = mismatch.Offered.Download?.SourceName ?? "the source";
        CanUseHeld = mismatch.CanUseHeld;

        Held = mismatch.Held switch
        {
            null => "Not a mod archive",
            LocalMod held when CanUseHeld => held.Version.Value,
            LocalMod held => $"Another mod: {held.Name}"
        };

        UseHeldText = CanUseHeld ? $"Use {Held}" : string.Empty;

        _useHeld = CanUseHeld;
    }


    public ModVersionIdentity Identity { get; }
    public string Name { get; }
    public string Picked { get; }
    public string Source { get; }

    /// <summary>What the download turned out to be.</summary>
    public string Held { get; }

    public bool CanUseHeld { get; }

    public string UseHeldText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeaveOut))]
    private bool _useHeld;

    public bool LeaveOut
    {
        get => UseHeld is false;
        set => UseHeld = value is false;
    }
}
