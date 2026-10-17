using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.Shell.Modals;
using System.ComponentModel;

namespace ModsDude.Client.Wpf.Profiles;

/// <summary>
/// The versions this profile has dismissed, with a checkbox each to offer it as an update again.
/// Every box starts unchecked, so dismissing this modal changes nothing.
/// </summary>
public partial class ProfileDismissedVersionsModalViewModel : ModalViewModel
{
    public ProfileDismissedVersionsModalViewModel(IReadOnlyList<ProfileDismissedVersionViewModel> items)
    {
        Items = items;

        foreach (var item in items)
        {
            item.PropertyChanged += OnItemChanged;
        }
    }


    public IReadOnlyList<ProfileDismissedVersionViewModel> Items { get; }

    public string Title => "Dismissed versions";

    public string Message => "Dismissed versions are never offered as updates. Pick the ones to offer again.";

    /// <summary>Empty until something is confirmed, so a dismissed modal restores nothing.</summary>
    public IReadOnlyList<ModVersionIdentity> Result { get; private set; } = [];

    public int SelectedCount => Items.Count(x => x.IsSelected);

    public string ConfirmText => SelectedCount switch
    {
        0 => "Restore nothing",
        1 => "Restore 1 version",
        _ => $"Restore {SelectedCount} versions"
    };


    [RelayCommand]
    private void Confirm()
    {
        Result = [.. Items.Where(x => x.IsSelected).Select(x => x.Identity)];
        Done = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        Done = true;
    }


    public override bool TryCancel() => Press(CancelCommand);

    public override bool TryAccept() => Press(ConfirmCommand);


    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ProfileDismissedVersionViewModel.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ConfirmText));
    }
}

public partial class ProfileDismissedVersionViewModel(string name, ModVersionIdentity identity) : ObservableObject
{
    public string Name { get; } = name;
    public ModVersionIdentity Identity { get; } = identity;

    public string VersionText => Identity.VersionId.Value;

    /// <summary>Unchecked, deliberately: this modal defaults to changing nothing.</summary>
    [ObservableProperty]
    private bool _isSelected;
}
