using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Profiles;
using ModsDude.Client.Wpf.Shell.Modals;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>
/// The locked mods with a newer version, with a checkbox each and what moving one would risk.
/// </summary>
/// <remarks>
/// Reached deliberately, through the updates band's link to them, rather than fired at
/// every save. The same list shown automatically would re-ask a question the user answered when they
/// locked these mods, which is how a safety prompt turns into noise people learn to dismiss.
/// Every box starts unchecked, so dismissing this changes nothing.
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
        ? "One locked mod in this profile has a newer version."
        : $"{Items.Count} locked mods in this profile have newer versions.";

    public string Warning => "Changing these versions can break savegames.";

    /// <summary>Empty until something is confirmed, so a dismissed modal moves nothing.</summary>
    public IReadOnlyList<ModKey> Result { get; private set; } = [];

    public int SelectedCount => Items.Count(x => x.IsSelected);

    public string ConfirmText => SelectedCount switch
    {
        0 => "Update",
        1 => "Update 1 mod",
        _ => $"Update {SelectedCount} mods"
    };


    private bool CanConfirm() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
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


    public override bool IsDismissable => true;

    public override bool TryCancel() => Press(CancelCommand);

    public override bool TryAccept() => Press(ConfirmCommand);


    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ProfileLockedUpdateViewModel.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ConfirmText));
        ConfirmCommand.NotifyCanExecuteChanged();
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

    /// <summary>Unchecked, deliberately: this modal defaults to changing nothing.</summary>
    [ObservableProperty]
    private bool _isSelected;


    private static string Describe(ProfileModLock @lock) => @lock.Source switch
    {
        ProfileModLockSource.Adapter =>
            "Version-sensitive. Changing it partway through a savegame can break that savegame.",
        ProfileModLockSource.Profile =>
            "Locked in this profile.",
        _ =>
            "Version-sensitive and locked in this profile. Changing it partway through a savegame can break that savegame.",
    };
}
