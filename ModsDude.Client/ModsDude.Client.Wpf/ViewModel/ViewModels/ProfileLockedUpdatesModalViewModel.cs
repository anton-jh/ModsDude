using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

/// <summary>
/// The locked mods a batch update left alone, with a checkbox each and what moving one would risk.
/// </summary>
/// <remarks>
/// Reached deliberately, through the skipped count the batch action reports, rather than fired at
/// every save. The same list shown automatically would re-ask a question the user answered when they
/// locked these mods, which is how a safety prompt turns into noise people learn to dismiss.
/// Every box starts unchecked, so dismissing this changes nothing.
/// See docs/09-mod-catalog.md#batch-updates-skip-locked-mods-entirely.
/// </remarks>
public partial class ProfileLockedUpdatesModalViewModel : ModalViewModel
{
    public ProfileLockedUpdatesModalViewModel(IReadOnlyList<ProfileLockedUpdateViewModel> items)
    {
        Items = items;

        foreach (var item in items)
        {
            item.PropertyChanged += OnItemChanged;
        }
    }


    public IReadOnlyList<ProfileLockedUpdateViewModel> Items { get; }

    public string Title => "Locked mods";

    public string Message => Items.Count == 1
        ? "One mod in this profile has a newer version and was skipped because it is locked."
        : $"{Items.Count} mods in this profile have newer versions and were skipped because they are locked.";

    public string Warning => "Locking exists because changing these versions can break things that are "
        + "expensive to undo. Pick the ones you mean to move.";

    /// <summary>Empty until something is confirmed, so a dismissed dialog moves nothing.</summary>
    public IReadOnlyList<ModKey> Result { get; private set; } = [];

    public int SelectedCount => Items.Count(x => x.IsSelected);

    public string ConfirmText => SelectedCount switch
    {
        0 => "Update nothing",
        1 => "Update 1 mod",
        _ => $"Update {SelectedCount} mods"
    };


    [RelayCommand]
    private void Confirm()
    {
        Result = [.. Items.Where(x => x.IsSelected).Select(x => x.ModId)];
        Done = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        Done = true;
    }


    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ProfileLockedUpdateViewModel.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ConfirmText));
    }
}

/// <summary>One locked mod, and the consequence of moving it, spelled out per mod.</summary>
public partial class ProfileLockedUpdateViewModel : ObservableObject
{
    public ProfileLockedUpdateViewModel(string name, ProfileModUpdate update)
    {
        Name = name;
        ModId = update.ModId;
        From = update.From;
        To = update.To;
        Consequence = Describe(update.Lock);
    }


    public string Name { get; }
    public ModKey ModId { get; }
    public ModVersionKey From { get; }
    public ModVersionKey To { get; }
    public string Consequence { get; }

    public string VersionText => $"{From} → {To}";

    /// <summary>Unchecked, deliberately: this dialog defaults to changing nothing.</summary>
    [ObservableProperty]
    private bool _isSelected;


    private static string Describe(ProfileModLock @lock) => @lock.Source switch
    {
        ProfileModLockSource.Adapter =>
            "Version-sensitive, as the game adapter read it from the mod file - a map, typically. Changing the "
                + "version partway through a save can corrupt that save, and the damage tends to show up long after.",
        ProfileModLockSource.Profile =>
            "You locked this mod in this profile. Updating it here undoes that decision for this profile only.",
        _ =>
            "Version-sensitive, as the game adapter read it from the mod file, and locked in this profile as well. "
                + "Changing the version partway through a save can corrupt that save.",
    };
}
