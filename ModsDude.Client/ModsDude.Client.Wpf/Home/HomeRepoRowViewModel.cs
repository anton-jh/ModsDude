using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Wpf.Friends;
using ModsDude.Client.Wpf.Repos;

namespace ModsDude.Client.Wpf.Home;

/// <summary>
/// One repo on Home. Kept across redraws and handed each new <see cref="HomeRepoState"/>, so the
/// profile picker stays open while what it lists changes underneath it.
/// </summary>
public sealed partial class HomeRepoRowViewModel(HomeRepoListViewModel owner, HomeRepoState state) : ObservableObject
{
    private const int _collapsedFriendsCount = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerEmptyText))]
    [NotifyPropertyChangedFor(nameof(HasPickerEmptyText))]
    [NotifyPropertyChangedFor(nameof(VisibleFriends))]
    [NotifyPropertyChangedFor(nameof(CanExpandFriends))]
    [NotifyPropertyChangedFor(nameof(FriendsToggleText))]
    private HomeRepoState _state = state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleFriends))]
    [NotifyPropertyChangedFor(nameof(FriendsToggleText))]
    private bool _isFriendsExpanded;

    [ObservableProperty]
    private bool _isPickerOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerEmptyText))]
    [NotifyPropertyChangedFor(nameof(HasPickerEmptyText))]
    private bool _profilesFailed;

    public Guid RepoId => State.RepoId;

    public string? PickerEmptyText => State.HasProfiles
        ? null
        : State.ProfilesLoaded ? "No profiles"
        : ProfilesFailed ? "Could not load the profiles"
        : "Loading…";

    public bool HasPickerEmptyText => PickerEmptyText is not null;

    public IReadOnlyList<FriendProfileGroupViewModel> VisibleFriends => IsFriendsExpanded
        ? State.Friends
        : [.. State.Friends.Take(_collapsedFriendsCount)];

    public bool CanExpandFriends => State.Friends.Count > _collapsedFriendsCount;

    public string FriendsToggleText => IsFriendsExpanded
        ? "Show fewer"
        : $"Show {State.Friends.Count - _collapsedFriendsCount} more";


    /// <remarks>Concurrent, so the toggle stays usable to close the picker while a read is under way.</remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OpenPicker(CancellationToken cancellationToken)
        => IsPickerOpen ? owner.LoadProfilesAsync(this, cancellationToken) : Task.CompletedTask;

    [RelayCommand]
    private Task PickProfile(HomeProfileOption option)
    {
        IsPickerOpen = false;

        return owner.ActivateAsync(this, option);
    }

    [RelayCommand]
    private Task RunAction() => owner.RunActionAsync(this);

    [RelayCommand]
    private Task OpenOverview() => owner.OpenSectionAsync(this, RepoSection.Overview);

    [RelayCommand]
    private Task OpenSection(RepoSectionAccess section) => owner.OpenSectionAsync(this, section.Section);

    [RelayCommand]
    private Task CheckIn(HomeHeldSavegame savegame) => owner.CheckInAsync(savegame);

    [RelayCommand]
    private void ToggleFriends() => IsFriendsExpanded = !IsFriendsExpanded;
}
